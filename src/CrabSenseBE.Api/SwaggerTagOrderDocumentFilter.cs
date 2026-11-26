using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CrabSenseBE.Api;

/// <summary>
/// Keep the Swagger UI organized by business capability, not by controller class
/// or legacy [Tags] annotations. This only changes OpenAPI presentation:
/// API paths, actions, security and contracts remain unchanged.
/// </summary>
public sealed class SwaggerTagOrderDocumentFilter : IDocumentFilter
{
    public const string UnclassifiedGroup = "99. Other APIs (needs classification)";

    private static readonly string[] Ordered =
    [
        "00. Authentication",
        "01. Users & Permissions",
        "10. Farm - Farming Areas",
        "11. Farm - Farming Rows",
        "12. Farm - Boxes",
        "13. Farm - Crabs, Lots & Health",
        "14. Farm - Operations & Inspections",
        "15. Farm - QR & Traceability",
        "16. Farm - Harvest & Frozen Storage",
        "20. IoT - Sensors",
        "21. IoT - Controllers & Devices",
        "22. IoT - Kiosks & Edge Sync",
        "23. IoT - RAS & Water Systems",
        "24. Water - Chemical Analysis",
        "25. Water - Salinity & Minerals",
        "30. Cameras - Management",
        "31. Cameras - Recording Schedules",
        "32. Cameras - Recordings & Runtime",
        "40. AI - Services & Runtime",
        "41. AI - Inference & Training",
        "50. Monitoring - Device Health",
        "51. Monitoring - Alerts & Thresholds",
        "52. Monitoring - Notifications",
        "60. Sales - Customers & Pricing",
        "61. Sales - Orders & Payments",
        "70. Dashboard & Reports",
        "80. System - Configuration & Security",
        "81. System - Backups & Storage",
        "82. System - Media",
        UnclassifiedGroup
    ];

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context) => Rewrite(swaggerDoc);

    // Replaces both legacy [Tags] values and implicit controller-name tags.
    public static void Rewrite(OpenApiDocument swaggerDoc)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (route, pathItem) in swaggerDoc.Paths)
        {
            var group = GroupForRoute(route);
            foreach (var operation in pathItem.Operations.Values)
            {
                operation.Tags = [new OpenApiTag { Name = group }];
                used.Add(group);
            }
        }

        swaggerDoc.Tags = Ordered.Where(used.Contains)
            .Concat(used.Except(Ordered, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
            .Select(name => new OpenApiTag { Name = name })
            .ToList();
    }

    // Match endpoint paths, not controller names: one controller may own several
    // business areas, and multiple controllers may contribute to the same area.
    public static string GroupForRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return UnclassifiedGroup;
        var path = route.TrimStart('~', '/').Split('?', 2)[0].TrimEnd('/').ToLowerInvariant();
        bool Is(string prefix) => path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);
        bool Contains(string part) => path.Contains(part, StringComparison.Ordinal);

        if (Is("api/auth")) return "00. Authentication";
        if (Is("api/users") || Is("api/farm-users")) return "01. Users & Permissions";

        // Nested routes must be classified before their broad /areas, /boxes,
        // /harvest-vouchers or /frozen-lots prefixes.
        if ((Is("api/areas") && (Contains("/water-analysis") || Contains("/water-bench"))) || Is("api/edge/water-bench"))
            return "24. Water - Chemical Analysis";
        if (Is("api/areas") && Contains("/ras-flow")) return "23. IoT - RAS & Water Systems";
        if (Is("api/box-qr") || Is("api/traceability") || Is("api/traceability-qrs")
            || (Is("api/boxes") && Contains("/qr"))
            || ((Is("api/frozen-lots") || Is("api/harvest-vouchers")) && Contains("/qr")))
            return "15. Farm - QR & Traceability";
        if (Is("api/boxes") && Contains("/harvest-vouchers"))
            return "16. Farm - Harvest & Frozen Storage";
        if ((Is("api/boxes") || Is("api/crabs"))
            && (Contains("/allocations") || Contains("/moltings") || Contains("/status-history")
                || Contains("/farming-status") || Contains("/farming-timeline")))
            return "14. Farm - Operations & Inspections";

        if (Is("api/farming-areas")) return "10. Farm - Farming Areas";
        if (Is("api/farming-rows")) return "11. Farm - Farming Rows";
        if (Is("api/boxes")) return "12. Farm - Boxes";
        if (Is("api/crabs") || Is("api/crab") || Is("api/crab-condition")
            || Is("api/crab-lots") || Is("api/mortality"))
            return "13. Farm - Crabs, Lots & Health";
        if (Is("api/operations") || Is("api/inspections") || Is("api/allocations")
            || Is("api/moltings") || Is("api/box-status-histories"))
            return "14. Farm - Operations & Inspections";
        if (Is("api/harvest-vouchers") || Is("api/harvest-assignments") || Is("api/frozen-lots") || Is("api/frozen-storage-transactions")
            || Is("api/frozen-inventory-adjustments") || Is("api/frozen-opening-balances"))
            return "16. Farm - Harvest & Frozen Storage";

        if (Is("api/sensors") || Is("api/farm/sensors") || Is("api/iot/sensors")
            || Is("api/iot/sensor-tests") || Is("api/iot/sensor-data") || Is("api/iot/live")
            || Is("api/iot/history"))
            return "20. IoT - Sensors";
        if (Is("api/controllers") || Is("api/devices") || Is("api/iot/controllers")
            || Is("api/iot/relay-state"))
            return "21. IoT - Controllers & Devices";
        if (Is("api/kiosks") || Is("api/provision") || Is("api/sync")
            || Is("api/v1/sync") || Is("api/v1/synchronization") || Is("api/edge/commands"))
            return "22. IoT - Kiosks & Edge Sync";
        if (Is("api/areas") || Is("api/water-systems"))
            return "23. IoT - RAS & Water Systems";
        if (Is("api/mineral-dosing")) return "25. Water - Salinity & Minerals";

        if (Is("api/camera-schedules") || Is("api/cameras/recording-schedules"))
            return "31. Cameras - Recording Schedules";
        if (Is("api/cameras") && Contains("/recording"))
            return "32. Cameras - Recordings & Runtime";
        if (Is("api/cameras")) return "30. Cameras - Management";

        if (Is("api/ai/services") || Is("api/ai/runtime"))
            return "40. AI - Services & Runtime";
        if (Is("api/ai") || Is("api/training-data"))
            return "41. AI - Inference & Training";

        if (Is("api/system/device-monitoring") || Is("api/system/device-locations"))
            return "50. Monitoring - Device Health";
        if (Is("api/alerts") || Is("api/alert-thresholds") || Is("api/system/alerts"))
            return "51. Monitoring - Alerts & Thresholds";
        if (Is("api/notifications") || Is("api/v1/notifications") || Is("api/notification-rules"))
            return "52. Monitoring - Notifications";

        if (Is("api/customers") || Is("api/price-lists"))
            return "60. Sales - Customers & Pricing";
        if (Is("api/sales") || Is("api/sales-orders") || Is("api/deliveries")
            || Is("api/payments"))
            return "61. Sales - Orders & Payments";
        if (Is("api/dashboard") || Is("api/reports"))
            return "70. Dashboard & Reports";

        if (Is("api/admin/db") || Is("api/storage"))
            return "81. System - Backups & Storage";
        if (Is("api/media"))
            return "82. System - Media";
        if (Is("api/system") || Is("api/security") || Is("api/settings") || Is("health"))
            return "80. System - Configuration & Security";

        return UnclassifiedGroup;
    }
}
