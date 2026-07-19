using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CrabSenseBE.Api;

/// <summary>Ordered Swagger sidebar tags.</summary>
public sealed class SwaggerTagOrderDocumentFilter : IDocumentFilter
{
    private static readonly string[] Ordered =
    [
        "00. Auth",
        "01. CRUD — Farming Areas",
        "02. CRUD — Farming Rows",
        "03. CRUD — Crab Farm Boxes",
        "04. CRUD — Crabs",
        "04b. History — Allocation & Molting",
        "05. CRUD — Crab Lots",
        "06. CRUD — Crop Batches",
        "07. Box QR (scan)",
        "08. IoT — Sensor data",
        "08. IoT — Sensors",
        "09. IoT — Devices",
        "09. IoT — Edge sync",
        "10. Alerts",
        "11. Notifications",
        "12. Media (Drive)",
        "20. CRUD — Harvest (stub)",
        "21. CRUD — Frozen Lots (stub)",
        "22. CRUD — Customers (stub)",
        "23. CRUD — Price Lists (stub)",
        "24. CRUD — Sales Orders (stub)",
        "25. CRUD — Deliveries (stub)",
        "26. CRUD — Payments (stub)",
        "30. Reports (stub)",
        "31. Dashboard (stub)",
        "32. CRUD — Settings (stub)",
        "33. AI (stub)",
    ];

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in swaggerDoc.Paths.Values)
        {
            foreach (var op in path.Operations.Values)
            {
                if (op.Tags is null) continue;
                foreach (var tag in op.Tags)
                {
                    var name = tag?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        used.Add(name);
                }
            }
        }

        swaggerDoc.Tags = Ordered
            .Where(used.Contains)
            .Concat(used.Except(Ordered, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            .Select(name => new OpenApiTag { Name = name })
            .ToList();
    }
}
