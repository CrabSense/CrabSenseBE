using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Sales;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class SalesOrderService : ISalesOrderService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public SalesOrderService(IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<IEnumerable<CustomerDto>>> GetCustomersAsync(
        CancellationToken ct = default)
    {
        var rows = (await _uow.Customers.GetAllAsync(ct))
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(MapCustomer)
            .ToList();
        return ApiResponse<IEnumerable<CustomerDto>>.Ok(rows);
    }

    public async Task<ApiResponse<CustomerDto>> UpsertCustomerAsync(
        UpsertCustomerRequest request,
        CancellationToken ct = default)
    {
        var customer = await FindOrCreateCustomerAsync(
            request.Name,
            request.Phone,
            request.Email,
            request.Address,
            request.CustomerType,
            ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<CustomerDto>.Ok(MapCustomer(customer));
    }

    public async Task<ApiResponse<IEnumerable<SalesOrderDto>>> GetOrdersAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var orders = (await _uow.SalesOrders.GetAllAsync(ct))
            .Where(o =>
                farmingAreaId is null
                || farmingAreaId == Guid.Empty
                || o.FarmingAreaId == farmingAreaId)
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.CreatedAt)
            .ToList();
        var mapped = new List<SalesOrderDto>();
        foreach (var order in orders)
            mapped.Add(await MapOrderAsync(order, ct));
        return ApiResponse<IEnumerable<SalesOrderDto>>.Ok(mapped);
    }

    public async Task<ApiResponse<SalesOrderDto>> GetOrderByIdAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var order = await _uow.SalesOrders.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("SalesOrder");
        return ApiResponse<SalesOrderDto>.Ok(await MapOrderAsync(order, ct));
    }

    public async Task<ApiResponse<SalesOrderDto>> CreateOrderAsync(
        CreateSalesOrderRequest request,
        CancellationToken ct = default)
    {
        var lines = request.Lines?.ToList()
            ?? throw AppException.BadRequest("Danh sách cua bán là bắt buộc.");
        if (lines.Count == 0)
            throw AppException.BadRequest("Chọn ít nhất một con cua để bán.");
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw AppException.BadRequest("Tên khách hàng là bắt buộc.");

        var duplicate = lines.GroupBy(l => l.CrabId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw AppException.BadRequest($"Cua '{duplicate.Key}' bị chọn trùng.");

        var customer = await FindOrCreateCustomerAsync(
            request.CustomerName,
            request.CustomerPhone,
            null,
            null,
            null,
            ct);

        var payment = ParsePayment(request.PaymentStatus);
        var seller = string.IsNullOrWhiteSpace(request.SellerName)
            ? await ResolveUserNameAsync(ct)
            : request.SellerName.Trim();

        var order = new SalesOrder
        {
            OrderCode = await GenerateOrderCodeAsync(ct),
            CustomerId = customer.Id,
            OrderDate = NormalizeUtc(request.OrderDate ?? DateTime.UtcNow),
            Status = payment == PaymentStatus.Paid
                ? OrderStatus.Completed
                : OrderStatus.Confirmed,
            PaymentStatus = payment,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedBy = _currentUser.UserId,
            FarmingAreaId = request.FarmingAreaId,
            SellerName = seller
        };

        decimal total = 0;
        foreach (var lineReq in lines)
        {
            var crab = await _uow.Crabs.GetByIdAsync(lineReq.CrabId, ct)
                ?? throw AppException.BadRequest($"Cua '{lineReq.CrabId}' không tồn tại.");
            if (crab.Status != CrabStatus.Harvested)
                throw AppException.Conflict(
                    $"Cua '{crab.Code}' chưa ở tồn kho (Đã thu hoạch). Không bán cua đang nuôi.");

            var alreadySold = await _uow.SalesOrderLines.AnyAsync(
                l => l.CrabId == crab.Id, ct);
            if (alreadySold)
                throw AppException.Conflict($"Cua '{crab.Code}' đã nằm trong đơn khác.");

            var weight = lineReq.WeightGram ?? crab.WeightGram ?? 0;
            if (weight <= 0)
                throw AppException.BadRequest($"Cua '{crab.Code}' thiếu trọng lượng.");
            var price = lineReq.UnitPricePerKg ?? 0;
            if (price < 0)
                throw AppException.BadRequest("Đơn giá phải >= 0.");

            var qtyKg = decimal.Round(weight / 1000m, 3, MidpointRounding.AwayFromZero);
            var amount = decimal.Round(qtyKg * price, 0, MidpointRounding.AwayFromZero);
            total += amount;

            order.Lines.Add(new SalesOrderLine
            {
                SalesOrderId = order.Id,
                CrabId = crab.Id,
                CrabCode = crab.Code,
                Grade = string.IsNullOrWhiteSpace(lineReq.Grade) ? null : lineReq.Grade.Trim(),
                Quantity = 1,
                QuantityKg = qtyKg,
                UnitPricePerKg = price,
                TotalAmount = amount
            });

            var oldStatus = crab.Status;
            var oldCondition = crab.Condition;
            crab.Status = CrabStatus.Sold;
            crab.Condition = CrabCondition.Sold;
            crab.WeightGram = weight;
            _uow.Crabs.Update(crab);

            await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
            {
                CrabId = crab.Id,
                OldStatus = oldStatus,
                NewStatus = CrabStatus.Sold,
                OldCondition = oldCondition,
                NewCondition = CrabCondition.Sold,
                ChangedAt = DateTime.UtcNow,
                Source = "sale",
                Reason = order.OrderCode,
                ChangedByUserId = _currentUser.UserId
            }, ct);
        }

        order.TotalAmount = total;
        await _uow.SalesOrders.AddAsync(order, ct);

        if (payment == PaymentStatus.Paid)
        {
            await _uow.Payments.AddAsync(new Payment
            {
                SalesOrderId = order.Id,
                Amount = total,
                Method = "cash",
                PaidAt = order.OrderDate,
                Status = PaymentStatus.Paid
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SalesOrderDto>.Ok(
            await MapOrderAsync(order, ct),
            $"Đơn '{order.OrderCode}' đã tạo. Cua chuyển sang Đã bán.");
    }

    public async Task<ApiResponse<SalesOverviewDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var orders = (await _uow.SalesOrders.GetAllAsync(ct))
            .Where(o =>
                o.Status != OrderStatus.Cancelled
                && (farmingAreaId is null
                    || farmingAreaId == Guid.Empty
                    || o.FarmingAreaId == farmingAreaId))
            .ToList();
        var todayOrders = orders
            .Where(o => o.OrderDate >= today && o.OrderDate < tomorrow)
            .ToList();
        var todayIds = todayOrders.Select(o => o.Id).ToHashSet();
        var soldToday = todayIds.Count == 0
            ? 0
            : (await _uow.SalesOrderLines.FindAsync(
                    l => todayIds.Contains(l.SalesOrderId), ct))
                .Sum(l => l.Quantity <= 0 ? 1 : l.Quantity);

        var inventory = await GetInventoryAsync(farmingAreaId, ct);
        var items = inventory.Data?.ToList() ?? [];
        return ApiResponse<SalesOverviewDto>.Ok(new SalesOverviewDto(
            todayOrders.Sum(o => o.TotalAmount),
            soldToday,
            items.Count,
            decimal.Round(items.Sum(i => i.WeightGram ?? 0) / 1000m, 3)));
    }

    public async Task<ApiResponse<IEnumerable<InventoryCrabDto>>> GetInventoryAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var harvested = (await _uow.Crabs.FindAsync(c => c.Status == CrabStatus.Harvested, ct))
            .ToList();
        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var voucherIds = (await _uow.HarvestVouchers.FindAsync(
                    v => v.FarmingAreaId == areaId && v.Status == HarvestStatus.Completed, ct))
                .Select(v => v.Id)
                .ToHashSet();
            var crabIds = voucherIds.Count == 0
                ? new HashSet<Guid>()
                : (await _uow.HarvestLines.FindAsync(
                        l => l.CrabId != null && voucherIds.Contains(l.HarvestVoucherId),
                        ct))
                    .Select(l => l.CrabId!.Value)
                    .ToHashSet();
            harvested = harvested.Where(c => crabIds.Contains(c.Id)).ToList();
        }

        var histories = harvested.Count == 0
            ? []
            : (await _uow.CrabHarvestHistories.GetAllAsync(ct))
                .Where(h => harvested.Any(c => c.Id == h.CrabId))
                .ToList();
        var latest = histories
            .GroupBy(h => h.CrabId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.HarvestedAt).First());

        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToDictionary(b => b.Id, b => b.Code);
        var rows = harvested
            .OrderByDescending(c => latest.GetValueOrDefault(c.Id)?.HarvestedAt ?? c.UpdatedAt)
            .Select(c =>
            {
                latest.TryGetValue(c.Id, out var hist);
                string? boxCode = null;
                if (c.BoxId is Guid bid)
                    boxes.TryGetValue(bid, out boxCode);
                return new InventoryCrabDto(
                    c.Id,
                    c.Code,
                    boxCode,
                    c.WeightGram ?? hist?.WeightGram,
                    hist?.Grade,
                    c.Condition.ToString(),
                    hist?.HarvestedAt);
            })
            .ToList();
        return ApiResponse<IEnumerable<InventoryCrabDto>>.Ok(rows);
    }

    private async Task<Customer> FindOrCreateCustomerAsync(
        string name,
        string? phone,
        string? email,
        string? address,
        string? customerType,
        CancellationToken ct)
    {
        var trimmedName = name.Trim();
        var trimmedPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        var existing = (await _uow.Customers.GetAllAsync(ct)).FirstOrDefault(c =>
            (trimmedPhone != null
                && !string.IsNullOrWhiteSpace(c.Phone)
                && string.Equals(c.Phone.Trim(), trimmedPhone, StringComparison.OrdinalIgnoreCase))
            || string.Equals(c.Name.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (trimmedPhone is not null) existing.Phone = trimmedPhone;
            if (!string.IsNullOrWhiteSpace(email)) existing.Email = email.Trim();
            if (!string.IsNullOrWhiteSpace(address)) existing.Address = address.Trim();
            existing.IsActive = true;
            _uow.Customers.Update(existing);
            return existing;
        }

        var created = new Customer
        {
            Name = trimmedName,
            Phone = trimmedPhone,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            CustomerType = string.IsNullOrWhiteSpace(customerType) ? "retail" : customerType.Trim(),
            IsActive = true
        };
        await _uow.Customers.AddAsync(created, ct);
        return created;
    }

    private async Task<SalesOrderDto> MapOrderAsync(SalesOrder order, CancellationToken ct)
    {
        var customer = await _uow.Customers.GetByIdAsync(order.CustomerId, ct);
        var lines = order.Lines.Count > 0
            ? order.Lines
            : (await _uow.SalesOrderLines.FindAsync(l => l.SalesOrderId == order.Id, ct)).ToList();
        return new SalesOrderDto(
            order.Id,
            order.OrderCode,
            order.OrderDate,
            order.Status.ToString(),
            order.PaymentStatus.ToString(),
            order.TotalAmount,
            lines.Sum(l => l.Quantity <= 0 ? 1 : l.Quantity),
            order.CustomerId,
            customer?.Name ?? "",
            customer?.Phone,
            order.SellerName,
            order.FarmingAreaId,
            order.Notes,
            lines.Select(l => new SalesOrderLineDto(
                l.Id,
                l.CrabId,
                l.CrabCode,
                l.Grade,
                l.Quantity <= 0 ? 1 : l.Quantity,
                decimal.Round(l.QuantityKg * 1000m, 1),
                l.QuantityKg,
                l.UnitPricePerKg,
                l.TotalAmount)).ToList());
    }

    private static CustomerDto MapCustomer(Customer c) =>
        new(c.Id, c.Name, c.Phone, c.Email, c.Address, c.CustomerType, c.IsActive);

    private static PaymentStatus ParsePayment(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return PaymentStatus.Pending;
        return Enum.TryParse<PaymentStatus>(raw.Trim(), true, out var parsed)
            ? parsed
            : PaymentStatus.Pending;
    }

    private async Task<string> GenerateOrderCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
        {
            var code = $"HD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
            if (!await _uow.SalesOrders.AnyAsync(o => o.OrderCode == code, ct))
                return code;
        }

        throw AppException.Conflict("Không tạo được mã đơn hàng.");
    }

    private async Task<string> ResolveUserNameAsync(CancellationToken ct)
    {
        try
        {
            var user = await _uow.Users.GetByIdAsync(_currentUser.UserId, ct);
            if (user is not null && !string.IsNullOrWhiteSpace(user.FullName))
                return user.FullName.Trim();
        }
        catch
        {
            // ignore
        }

        return "Owner";
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
