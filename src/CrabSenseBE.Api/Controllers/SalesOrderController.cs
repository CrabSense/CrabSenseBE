using CrabSenseBE.Application.DTOs.Sales;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
[Tags("22. CRUD — Customers")]
[Produces("application/json")]
public class CustomersController : ControllerBase
{
    private readonly ISalesOrderService _service;

    public CustomersController(ISalesOrderService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetCustomersAsync(ct));

    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Upsert([FromBody] UpsertCustomerRequest request, CancellationToken ct)
        => Ok(await _service.UpsertCustomerAsync(request, ct));
}

[ApiController]
[Route("api/sales-orders")]
[Authorize]
[Tags("24. CRUD — Sales Orders")]
[Produces("application/json")]
public class SalesOrdersController : ControllerBase
{
    private readonly ISalesOrderService _service;

    public SalesOrdersController(ISalesOrderService service) => _service = service;

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] Guid? farmingAreaId,
        CancellationToken ct)
        => Ok(await _service.GetOverviewAsync(farmingAreaId, ct));

    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(
        [FromQuery] Guid? farmingAreaId,
        CancellationToken ct)
        => Ok(await _service.GetInventoryAsync(farmingAreaId, ct));

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? farmingAreaId,
        CancellationToken ct)
        => Ok(await _service.GetOrdersAsync(farmingAreaId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetOrderByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSalesOrderRequest request,
        CancellationToken ct)
        => Ok(await _service.CreateOrderAsync(request, ct));

    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
        => Ok(await _service.CompleteOrderAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Ok(await _service.CancelOrderAsync(id, ct));
}
