using CrabSenseBE.Application.Common;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            _logger.LogWarning("AppException: {Message}", ex.Message);
            await WriteResponse(context, ex.StatusCode, ex.Message);
        }
        catch (Google.GoogleApiException ex)
        {
            var mapped = CrabSenseBE.Infrastructure.Services.GoogleDriveMediaStorageService.TranslateDriveError(ex);
            _logger.LogWarning(ex, "Google Drive API error");
            await WriteResponse(context, mapped.StatusCode, mapped.Message);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent data update rejected");
            await WriteResponse(context, StatusCodes.Status409Conflict,
                "Dữ liệu đã thay đổi bởi request khác. Hãy tải lại và thử lại.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Database integrity constraint rejected the request");
            await WriteResponse(context, StatusCodes.Status409Conflict,
                "Thao tác xung đột với dữ liệu hiện tại hoặc một request đồng thời.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteResponse(context, 500, "An unexpected error occurred.");
        }
    }

    private static async Task WriteResponse(HttpContext context, int statusCode, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;
        var body = JsonSerializer.Serialize(ApiResponse.Fail(message), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await context.Response.WriteAsync(body);
    }
}
