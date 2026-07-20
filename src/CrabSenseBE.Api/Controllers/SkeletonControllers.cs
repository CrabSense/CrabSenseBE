using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/harvest-vouchers")]
[Authorize]
[Tags("20. CRUD — Harvest (stub)")]
[Produces("application/json")]
public class HarvestController : ControllerBase
{
    /// <summary>[READ] List harvest vouchers (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, message = "TODO", data = Array.Empty<object>() });

    /// <summary>[CREATE] Create harvest voucher (TODO)</summary>
    [HttpPost]
    public IActionResult Create() => StatusCode(501, new { message = "Not implemented yet" });
}

[ApiController]
[Route("api/frozen-lots")]
[Authorize]
[Tags("21. CRUD — Frozen Lots (stub)")]
[Produces("application/json")]
public class FrozenLotsController : ControllerBase
{
    /// <summary>[READ] List frozen lots (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, message = "TODO", data = Array.Empty<object>() });
}

[ApiController]
[Route("api/customers")]
[Authorize]
[Tags("22. CRUD — Customers (stub)")]
[Produces("application/json")]
public class CustomersController : ControllerBase
{
    /// <summary>[READ] List customers (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController]
[Route("api/price-lists")]
[Authorize]
[Tags("23. CRUD — Price Lists (stub)")]
[Produces("application/json")]
public class PriceListsController : ControllerBase
{
    /// <summary>[READ] List price lists (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController]
[Route("api/sales-orders")]
[Authorize]
[Tags("24. CRUD — Sales Orders (stub)")]
[Produces("application/json")]
public class SalesOrdersController : ControllerBase
{
    /// <summary>[READ] List sales orders (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController]
[Route("api/deliveries")]
[Authorize]
[Tags("25. CRUD — Deliveries (stub)")]
[Produces("application/json")]
public class DeliveriesController : ControllerBase
{
    /// <summary>[READ] List deliveries (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController]
[Route("api/payments")]
[Authorize]
[Tags("26. CRUD — Payments (stub)")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    /// <summary>[READ] List payments (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}


[ApiController]
[Route("api/dashboard")]
[Authorize]
[Tags("31. Dashboard (stub)")]
[Produces("application/json")]
public class DashboardController : ControllerBase
{
    /// <summary>[READ] Dashboard overview</summary>
    [HttpGet("overview")]
    public IActionResult Overview() => Ok(new { success = true, data = new object() });
}

[ApiController]
[Route("api/settings")]
[Authorize]
[Tags("32. CRUD — Settings (stub)")]
[Produces("application/json")]
public class SettingsController : ControllerBase
{
    /// <summary>[READ] List settings (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController]
[Route("api/ai")]
[Authorize]
[Tags("33. AI (stub)")]
[Produces("application/json")]
public class AiController : ControllerBase
{
    /// <summary>[READ] List AI detections</summary>
    [HttpGet("detections")]
    public IActionResult Detections() => Ok(new { success = true, data = Array.Empty<object>() });

    /// <summary>[CREATE] Submit AI feedback</summary>
    [HttpPost("feedback")]
    public IActionResult Feedback() => Ok(new { success = true });

    /// <summary>[READ] AI recommendations</summary>
    [HttpGet("recommendations")]
    public IActionResult Recommendations() => Ok(new { success = true, data = Array.Empty<object>() });
}
