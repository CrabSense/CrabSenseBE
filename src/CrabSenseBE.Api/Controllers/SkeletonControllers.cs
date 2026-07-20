using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>Skeleton controllers — implement service logic progressively</summary>

[ApiController] [Route("api/harvest-vouchers")] [Produces("application/json")]
public class HarvestController : ControllerBase
{
    // TODO: inject IHarvestService
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, message = "TODO", data = Array.Empty<object>() });
    [HttpPost] public IActionResult Create() => StatusCode(501, new { message = "Not implemented yet" });
}

[ApiController] [Route("api/frozen-lots")] [Produces("application/json")]
public class FrozenLotsController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, message = "TODO", data = Array.Empty<object>() });
}

[ApiController] [Route("api/qr-codes")] [Produces("application/json")]
public class QrController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
    [HttpGet("/api/traceability/{code}")] public IActionResult Lookup(string code)
        => Ok(new { success = true, message = $"Lookup for code: {code}", data = new object() });
}

[ApiController] [Route("api/customers")] [Produces("application/json")]
public class CustomersController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/price-lists")] [Produces("application/json")]
public class PriceListsController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/sales-orders")] [Produces("application/json")]
public class SalesOrdersController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/deliveries")] [Produces("application/json")]
public class DeliveriesController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/payments")] [Produces("application/json")]
public class PaymentsController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/alerts")] [Produces("application/json")]
public class AlertsController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/alert-thresholds")] [Produces("application/json")]
public class AlertThresholdsController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/notifications")] [Produces("application/json")]
public class NotificationsController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/dashboard")] [Produces("application/json")]
public class DashboardController : ControllerBase
{
    [HttpGet("overview")] public IActionResult Overview() => Ok(new { success = true, data = new object() });
}

[ApiController] [Route("api/settings")] [Produces("application/json")]
public class SettingsController : ControllerBase
{
    [HttpGet] public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController] [Route("api/ai")] [Produces("application/json")]
public class AiController : ControllerBase
{
    [HttpGet("detections")] public IActionResult Detections() => Ok(new { success = true, data = Array.Empty<object>() });
    [HttpPost("feedback")] public IActionResult Feedback() => Ok(new { success = true });
    [HttpGet("recommendations")] public IActionResult Recommendations() => Ok(new { success = true, data = Array.Empty<object>() });
}
