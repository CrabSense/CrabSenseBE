using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CrabSenseBE.Api;

/// <summary>
/// Swagger create examples: real linked hierarchy ids (not null).
/// Sample is loaded once and cached — never query DB for unrelated schemas.
/// </summary>
public sealed class FarmCreateSchemaFilter : ISchemaFilter
{
    private readonly IServiceScopeFactory _scopes;
    private static HierarchySample? _cache;
    private static readonly object Gate = new();

    public FarmCreateSchemaFilter(IServiceScopeFactory scopes) => _scopes = scopes;

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var t = context.Type;
        if (t != typeof(CreateFarmingAreaRequest)
            && t != typeof(CreateFarmingRowRequest)
            && t != typeof(CreateBoxRequest)
            && t != typeof(CreateCrabRequest)
            && t != typeof(AllocateCrabRequest))
            return;

        var sample = GetSample();

        if (t == typeof(CreateFarmingAreaRequest))
        {
            schema.Description =
                "Create KHU.\n" +
                "Required: name.\n" +
                "Code is NOT in body — API assigns AREA-A01, AREA-A02… from existing areas in DB.\n" +
                "OwnerId is NOT in body — API assigns from JWT.\n" +
                "Status: Active | Suspended | Closed (default Active).";
            SetRequired(schema, "name");
            schema.Example = Obj(
                ("name", new OpenApiString("Khu nuôi nhà 1")),
                ("location", new OpenApiString("Nhà nuôi số 1 - Tầng 1")),
                ("areaSquareMeters", new OpenApiDouble(100)),
                ("description", new OpenApiString("Khu nuôi cua lột")),
                ("status", new OpenApiString("Active")));
            Describe(schema, "name", "Tên khu — bắt buộc.");
            Describe(schema, "location", "Vị trí trong cơ sở. Ví dụ: Nhà nuôi số 1 - Tầng 1. Tùy chọn.");
            Describe(schema, "areaSquareMeters", "Diện tích khu (m²). Tùy chọn.");
            Describe(schema, "description", "Mô tả ngắn. Tùy chọn.");
            Describe(schema, "status", "Active = đang hoạt động; Suspended = tạm ngưng; Closed = ngừng hoạt động.");
            return;
        }

        if (t == typeof(CreateFarmingRowRequest))
        {
            schema.Description =
                "Create DÃY.\n" +
                "Required: farmingAreaId + name.\n" +
                "Code is NOT in body — API assigns DAY-A01, DAY-A02…\n" +
                "capacity = số hộp tối đa (0 = không giới hạn). Không tạo hộp sẵn.";
            SetRequired(schema, "farmingAreaId", "name");
            schema.Example = Obj(
                ("farmingAreaId", Uuid(sample.AreaId)),
                ("name", new OpenApiString("Dãy A")),
                ("location", new OpenApiString("Bên trái")),
                ("capacity", new OpenApiInteger(20)),
                ("description", new OpenApiString("Dãy nuôi cua lột")),
                ("status", new OpenApiString("Active")));
            Describe(schema, "farmingAreaId",
                $"Parent khu id — required. Sample: {sample.AreaName} ({sample.AreaId})");
            Describe(schema, "name", "Tên dãy — bắt buộc.");
            Describe(schema, "location", "Vị trí trong khu. Ví dụ: Bên trái. Tùy chọn.");
            Describe(schema, "capacity",
                "Số hộp tối đa. 0 = không giới hạn. Không tạo hộp khi tạo dãy.");
            Describe(schema, "description", "Mô tả ngắn. Tùy chọn.");
            Describe(schema, "status", "Active | Suspended | Closed (default Active).");
            ForceUuid(schema, "farmingAreaId");
            return;
        }

        if (t == typeof(CreateBoxRequest))
        {
            schema.Description =
                "Create HỘP.\n" +
                "Send farmingRowId — API assigns farmingAreaId from that dãy.\n" +
                "Example shows BOTH linked ids from DB.";
            SetRequired(schema, "farmingRowId");
            // Omit code in example — null looks broken; API auto BOX-0001 when omitted
            schema.Example = Obj(
                ("farmingRowId", Uuid(sample.RowId)),
                ("farmingAreaId", Uuid(sample.AreaId)));
            Describe(schema, "farmingRowId",
                $"Parent dãy — required. Sample: {sample.RowName} ({sample.RowId})");
            Describe(schema, "farmingAreaId",
                $"Auto from dãy if omitted. Sample linked khu: {sample.AreaName} ({sample.AreaId})");
            Describe(schema, "code",
                "OPTIONAL — leave empty/omit → API auto-generates BOX-0001, BOX-0002… (do not send null)");
            ForceUuid(schema, "farmingRowId");
            ForceUuid(schema, "farmingAreaId");
            return;
        }

        if (t == typeof(CreateCrabRequest))
        {
            schema.Description =
                "Place CUA.\n" +
                "Required: crabLotId + boxId (or autoAssignEmptyBox).\n" +
                "Code + QR tự sinh CRAB-0001 / QR-CRAB-0001.\n" +
                "condition: normal | premolt | molting | softshell | problem | dead | harvested.";
            SetRequired(schema, "crabLotId", "weightGram", "carapaceWidthMm", "carapaceLengthMm");
            schema.Example = Obj(
                ("crabLotId", Uuid(sample.LotId)),
                ("boxId", Uuid(sample.BoxId)),
                ("farmingRowId", Uuid(sample.RowId)),
                ("farmingAreaId", Uuid(sample.AreaId)),
                ("autoAssignEmptyBox", new OpenApiBoolean(false)),
                ("weightGram", new OpenApiDouble(120)),
                ("carapaceWidthMm", new OpenApiDouble(85)),
                ("carapaceLengthMm", new OpenApiDouble(72)),
                ("crabType", new OpenApiString("Cua biển")),
                ("gender", new OpenApiString("male")),
                ("initialCondition", new OpenApiString("Khỏe mạnh")),
                ("notes", new OpenApiString("Thả nuôi hộp A-001")),
                ("condition", new OpenApiString("normal")),
                ("moltingStage", new OpenApiString("hard-shell")));
            Describe(schema, "crabLotId", $"Required — lô. Sample: {sample.LotCode}");
            // Describe(schema, "cropBatchId", $"Required — vụ nuôi. Sample: {sample.BatchCode}");
            Describe(schema, "boxId", $"Hộp — Row/Area auto. Sample: {sample.BoxCode}");
            Describe(schema, "farmingRowId",
                $"Auto from box if omitted. Sample: {sample.RowName} ({sample.RowId})");
            Describe(schema, "farmingAreaId",
                $"Auto from box if omitted. Sample: {sample.AreaName} ({sample.AreaId})");
            Describe(schema, "moltingStage",
                "Shell stage of the crab: hard (default) = hard shell; after successful molt → softshell. Used to track softshell harvest readiness.");
            Describe(schema, "weightGram", "Required — cân nặng (g) > 0.");
            Describe(schema, "carapaceWidthMm", "Required — bề rộng mai (mm) > 0.");
            Describe(schema, "carapaceLengthMm", "Required — bề ngang mai (mm) > 0.");
            Describe(schema, "imageUrls",
                "Optional. Public S3 URLs from POST /api/crabs/images. Multiple photos OK.");
            ForceUuid(schema, "crabLotId");
            ForceUuid(schema, "cropBatchId");
            ForceUuid(schema, "boxId");
            ForceUuid(schema, "farmingRowId");
            ForceUuid(schema, "farmingAreaId");
            return;
        }

        // AllocateCrabRequest
        schema.Description = "Allocate crab to box — hierarchy ids should match.";
        SetRequired(schema, "crabId", "boxId");
        schema.Example = Obj(
            ("farmingAreaId", Uuid(sample.AreaId)),
            ("farmingRowId", Uuid(sample.RowId)),
            ("crabId", Uuid(sample.CrabId)),
            ("boxId", Uuid(sample.BoxId)),
            ("notes", new OpenApiString("move")));
    }

    private HierarchySample GetSample()
    {
        if (_cache is not null) return _cache;
        lock (Gate)
        {
            if (_cache is not null) return _cache;
            _cache = LoadSampleSafe();
            return _cache;
        }
    }

    private HierarchySample LoadSampleSafe()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromSeconds(5));

            var area = db.FarmingAreas.AsNoTracking()
                .OrderBy(a => a.Name).Select(a => new { a.Id, a.Name }).FirstOrDefault();

            var row = area is null
                ? null
                : db.FarmingRows.AsNoTracking()
                    .Where(r => r.FarmingAreaId == area.Id && r.IsActive)
                    .OrderBy(r => r.Name)
                    .Select(r => new { r.Id, r.Name, r.FarmingAreaId })
                    .FirstOrDefault()
                  ?? db.FarmingRows.AsNoTracking()
                    .OrderBy(r => r.Name)
                    .Select(r => new { r.Id, r.Name, r.FarmingAreaId })
                    .FirstOrDefault();

            var box = row is null
                ? null
                : db.Boxes.AsNoTracking()
                    .Where(b => b.FarmingRowId == row.Id)
                    .OrderBy(b => b.Code)
                    .Select(b => new { b.Id, b.Code, b.FarmingRowId })
                    .FirstOrDefault()
                  ?? db.Boxes.AsNoTracking()
                    .OrderBy(b => b.Code)
                    .Select(b => new { b.Id, b.Code, b.FarmingRowId })
                    .FirstOrDefault();

            if (box is not null && row is null)
            {
                row = db.FarmingRows.AsNoTracking()
                    .Where(r => r.Id == box.FarmingRowId)
                    .Select(r => new { r.Id, r.Name, r.FarmingAreaId })
                    .FirstOrDefault();
            }

            if (row is not null && area is null)
            {
                area = db.FarmingAreas.AsNoTracking()
                    .Where(a => a.Id == row.FarmingAreaId)
                    .Select(a => new { a.Id, a.Name })
                    .FirstOrDefault();
            }
            else if (row is not null && area is not null && row.FarmingAreaId != area.Id)
            {
                area = db.FarmingAreas.AsNoTracking()
                    .Where(a => a.Id == row.FarmingAreaId)
                    .Select(a => new { a.Id, a.Name })
                    .FirstOrDefault() ?? area;
            }

            var lot = db.CrabLots.AsNoTracking()
                .OrderBy(l => l.ImportDate)
                .Select(l => new { l.Id, l.LotCode })
                .FirstOrDefault();
            // var batch = db.CropBatches.AsNoTracking()
            //     .OrderBy(b => b.StartDate)
            //     .Select(b => new { b.Id, b.BatchCode })
            //     .FirstOrDefault();
            var crab = box is null
                ? null
                : db.Crabs.AsNoTracking()
                    .Where(c => c.BoxAllocations.Any(a => a.BoxId == box.Id && a.EndTime == null)).OrderBy(c => c.CreatedAt)
                    .Select(c => new { c.Id })
                    .FirstOrDefault();

            return new HierarchySample(
                AreaId: area?.Id ?? DemoGuid(1),
                AreaName: area?.Name ?? "(create khu first)",
                RowId: row?.Id ?? DemoGuid(2),
                RowName: row?.Name ?? "(create dãy first)",
                BoxId: box?.Id ?? DemoGuid(3),
                BoxCode: box?.Code ?? "(create box first)",
                LotId: lot?.Id ?? DemoGuid(4),
                LotCode: lot?.LotCode ?? "(create crab-lot first)",
                // BatchId: batch?.Id ?? DemoGuid(5),
                // BatchCode: batch?.BatchCode ?? "(create crop-batch first)",
                CrabId: crab?.Id ?? DemoGuid(5));
        }
        catch
        {
            return HierarchySample.Fallback();
        }
    }

    private static Guid DemoGuid(byte n) =>
        Guid.Parse($"3fa85f64-5717-4562-b3fc-2c963f66afa{n}");

    private sealed record HierarchySample(
        Guid AreaId, string AreaName,
        Guid RowId, string RowName,
        Guid BoxId, string BoxCode,
        Guid LotId, string LotCode,
        // Guid BatchId, string BatchCode,
        Guid CrabId)
    {
        public static HierarchySample Fallback() => new(
            DemoGuid(1), "sample-area",
            DemoGuid(2), "sample-row",
            DemoGuid(3), "sample-box",
            DemoGuid(4), "sample-lot",
            // DemoGuid(5), "sample-batch",
            DemoGuid(5));

        private static Guid DemoGuid(byte n) =>
            Guid.Parse($"3fa85f64-5717-4562-b3fc-2c963f66afa{n}");
    }

    private static OpenApiString Uuid(Guid id) => new(id.ToString());

    private static OpenApiObject Obj(params (string Key, IOpenApiAny Value)[] props)
    {
        var o = new OpenApiObject();
        foreach (var (key, value) in props)
            o[key] = value;
        return o;
    }

    private static void SetRequired(OpenApiSchema schema, params string[] names)
    {
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in names)
            schema.Required.Add(n);
    }

    private static void Describe(OpenApiSchema schema, string prop, string text)
    {
        if (schema.Properties is null) return;
        if (schema.Properties.TryGetValue(prop, out var p))
            p.Description = text;
    }

    private static void ForceUuid(OpenApiSchema schema, string prop)
    {
        if (schema.Properties is null) return;
        if (!schema.Properties.TryGetValue(prop, out var p)) return;
        p.Type = "string";
        p.Format = "uuid";
        p.Nullable = false;
    }
}
