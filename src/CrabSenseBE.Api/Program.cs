using CrabSenseBE.Api.Middleware;
using CrabSenseBE.Application;
using CrabSenseBE.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;

var builder = WebApplication.CreateBuilder(args);
LoadDotEnv(builder.Environment.ContentRootPath);
LoadDotEnv(Directory.GetCurrentDirectory());
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// ─── Serilog ────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/crabsense-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ─── Application + Infrastructure DI ───────────────────────────────────────
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped< IMortalityService, MortalityService>();

// ─── JWT Authentication ─────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required");
// Không cho chạy với secret placeholder/ngắn: kẻ tấn công biết secret là tự ký được token admin.
if (jwtSecret.Length < 32
    || jwtSecret.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Jwt:Secret must be at least 32 characters and must not be a CHANGE_ME placeholder.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ─── Rate limiting (chống brute-force đăng nhập) ────────────────────────────
// ponytail: partition theo IP, 20 lần / 5 phút. Nhiều user sau cùng NAT có thể
// dùng chung hạn mức; nâng cấp: partition thêm username khi tách được body.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"success\":false,\"message\":\"Quá nhiều lần thử đăng nhập. Vui lòng thử lại sau ít phút.\"}",
            ct);
    };
    options.AddPolicy("login", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));
});

// ─── Controllers + JSON ─────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// ─── Swagger / OpenAPI ──────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CrabSense API",
        Version = "v1",
        Description = "CrabSense/CrabGuardian backend API.\n\n" +
                      "Hierarchy on CREATE:\n" +
                      "1) Area (khu) — OwnerId from JWT (not in body)\n" +
                      "2) Row (dãy) — requires farmingAreaId\n" +
                      "3) Box (hộp) — requires farmingRowId (Area auto)\n" +
                      "4) Crab (cua) — requires boxId + crabLotId + cropBatchId (Row/Area auto from box)\n\n" +
                      "Each operation is prefixed with [READ]/[CREATE]/[UPDATE]/[DELETE]/[AUTH]/[ACTION]."
    });

    var xml = Path.Combine(AppContext.BaseDirectory, "CrabSenseBE.Api.xml");
    if (File.Exists(xml))
        c.IncludeXmlComments(xml, includeControllerXmlComments: true);

    c.DocumentFilter<CrabSenseBE.Api.SwaggerTagOrderDocumentFilter>();
    c.SchemaFilter<CrabSenseBE.Api.FarmCreateSchemaFilter>();

    // JWT: Http scheme → Swagger auto-adds "Bearer " (paste accessToken only)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Paste accessToken from /api/auth/login (do not type the word Bearer).",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ─── CORS ───────────────────────────────────────────────────────────────────
// Origin hợp lệ CHỈ đến từ config AllowedOrigins (không hardcode IP/tên miền).
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin)) return false;
                if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;

                // Dev: cho phép mọi cổng loopback để tooling local không phải thêm config.
                return builder.Environment.IsDevelopment()
                       && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                       && uri.IsLoopback;
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ─── Health checks ──────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

// ─── Middleware pipeline ─────────────────────────────────────────────────────
// Serilog phải nằm NGOÀI ExceptionMiddleware: exception nghiệp vụ (400/404) được
// chuyển thành response trước khi Serilog đọc StatusCode, nếu không sẽ log nhầm 500.
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();

// Swagger và HTTPS redirect đọc từ config, không phụ thuộc tên environment:
// production có thể bật Swagger (admin UI) mà không phải chạy ASPNETCORE_ENVIRONMENT=Development.
if (builder.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CrabSense API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowFrontend");
app.UseRateLimiter();
if (builder.Configuration.GetValue("Https:Redirect", false))
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

await DevDbBootstrap.InitializeAsync(app);

app.Run();

static void LoadDotEnv(string startDir)
{
    if (string.IsNullOrWhiteSpace(startDir)) return;
    var dir = new DirectoryInfo(Path.GetFullPath(startDir));
    for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
    {
        var path = Path.Combine(dir.FullName, ".env");
        if (!File.Exists(path)) continue;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"').Trim('\'');
            if (key.Length == 0) continue;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
        return;
    }
}
