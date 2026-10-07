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
        var lines = request.Lines?.ToList() ?? [];
        var orderStatus = ParseOrderStatus(request.OrderStatus);
        if (lines.Count == 0 && orderStatus != OrderStatus.Draft)
            throw AppException.BadRequest("Chọn ít nhất một con cua để bán.");
        if (string.IsNullOrWhiteSpace(request.CustomerName)
            && orderStatus != OrderStatus.Draft)
            throw AppException.BadRequest("Tên khách hàng là bắt buộc.");
        if (string.IsNullOrWhiteSpace(request.CustomerName)
            && orderStatus == OrderStatus.Draft)
            throw AppException.BadRequest("Nháp vẫn cần tên khách hàng.");

        var duplicate = lines.GroupBy(l => l.CrabId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw AppException.Conflict($"Cua '{duplicate.Key}' bị chọn trùng.");

        var customer = await FindOrCreateCustomerAsync(
            request.CustomerName,
            request.CustomerPhone,
            null,
            request.CustomerAddress,
            request.CustomerType,
            ct);
        var payment = ParsePayment(request.PaymentStatus, request.PaymentMethod);
        var method = NormalizePaymentMethod(request.PaymentMethod, payment);
        var delivery = NormalizeDelivery(request.DeliveryStatus);
        var seller = string.IsNullOrWhiteSpace(request.SellerName)
            ? await ResolveUserNameAsync(ct)
            : request.SellerName.Trim();
        var discount = decimal.Round(Math.Max(0, request.DiscountAmount ?? 0), 0, MidpointRounding.AwayFromZero);
        var shipping = decimal.Round(Math.Max(0, request.ShippingFee ?? 0), 0, MidpointRounding.AwayFromZero);

        var order = new SalesOrder
        {
            OrderCode = await GenerateOrderCodeAsync(ct),
            CustomerId = customer.Id,
            OrderDate = NormalizeUtc(request.OrderDate ?? DateTime.UtcNow),
            Status = orderStatus,
            PaymentStatus = payment,
            PaymentMethod = method,
            DeliveryStatus = delivery,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedBy = _currentUser.UserId,
            FarmingAreaId = request.FarmingAreaId,
            SellerName = seller,
            DiscountAmount = discount,
            ShippingFee = shipping
        };

        decimal subtotal = 0;
        foreach (var lineReq in lines)
        {
            var (line, amount) = await BuildLineAsync(order.Id, lineReq, ct);
            subtotal += amount;
            order.Lines.Add(line);
        }

        ApplyTotals(order, subtotal, request.PaidAmount);
        await _uow.SalesOrders.AddAsync(order, ct);

        if (orderStatus == OrderStatus.Completed)
            await MarkLinesSoldAsync(order, ct);

        if (order.PaymentStatus == PaymentStatus.Paid || order.PaymentStatus == PaymentStatus.Partial)
        {
            await _uow.Payments.AddAsync(new Payment
            {
                SalesOrderId = order.Id,
                Amount = order.PaidAmount,
                Method = method ?? "cash",
                PaidAt = order.OrderDate,
                Status = order.PaymentStatus
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);
        var message = orderStatus == OrderStatus.Draft
            ? $"Đơn '{order.OrderCode}' đã lưu nháp. Cua vẫn còn trong kho."
            : $"Đơn '{order.OrderCode}' đã xác nhận. Cua chuyển sang Đã bán.";
        return ApiResponse<SalesOrderDto>.Ok(await MapOrderAsync(order, ct), message);
    }

    public async Task<ApiResponse<SalesOrderDto>> CompleteOrderAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var order = await _uow.SalesOrders.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("SalesOrder");
        if (order.Status == OrderStatus.Cancelled)
            throw AppException.Conflict($"Đơn '{order.OrderCode}' đã hủy.");
        if (order.Status == OrderStatus.Completed)
            return ApiResponse<SalesOrderDto>.Ok(await MapOrderAsync(order, ct), "Đơn đã hoàn thành.");

        await LoadLinesAsync(order, ct);
        if (order.Lines.Count == 0)
            throw AppException.BadRequest("Đơn chưa có cua.");

        await MarkLinesSoldAsync(order, ct);
        order.Status = OrderStatus.Completed;
        _uow.SalesOrders.Update(order);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SalesOrderDto>.Ok(
            await MapOrderAsync(order, ct),
            $"Đơn '{order.OrderCode}' đã xác nhận. Cua chuyển sang Đã bán.");
    }

    public async Task<ApiResponse<SalesOrderDto>> CancelOrderAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var order = await _uow.SalesOrders.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("SalesOrder");
        if (order.Status == OrderStatus.Cancelled)
            return ApiResponse<SalesOrderDto>.Ok(await MapOrderAsync(order, ct), "Đơn đã hủy.");

        await LoadLinesAsync(order, ct);
        if (order.Status == OrderStatus.Completed)
            await RestoreLinesToInventoryAsync(order, ct);

        order.Status = OrderStatus.Cancelled;
        if (order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Partial)
            order.PaymentStatus = PaymentStatus.Cancelled;
        _uow.SalesOrders.Update(order);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SalesOrderDto>.Ok(
            await MapOrderAsync(order, ct),
            $"Đơn '{order.OrderCode}' đã hủy. Cua trở lại tồn kho nếu đã bán.");
    }

    public async Task<ApiResponse<SalesOverviewDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var orders = (await _uow.SalesOrders.GetAllAsync(ct))
            .Where(o =>
                o.Status == OrderStatus.Completed
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

        var allArea = (await _uow.SalesOrders.GetAllAsync(ct))
            .Where(o =>
                farmingAreaId is null
                || farmingAreaId == Guid.Empty
                || o.FarmingAreaId == farmingAreaId)
            .ToList();
        var yesterday = today.AddDays(-1);
        var yOrders = allArea
            .Where(o =>
                o.Status == OrderStatus.Completed
                && o.OrderDate >= yesterday
                && o.OrderDate < today)
            .ToList();
        var yIds = yOrders.Select(o => o.Id).ToHashSet();
        var soldYesterday = yIds.Count == 0
            ? 0
            : (await _uow.SalesOrderLines.FindAsync(
                    l => yIds.Contains(l.SalesOrderId), ct))
                .Sum(l => l.Quantity <= 0 ? 1 : l.Quantity);
        var yRevenue = yOrders.Sum(o => o.TotalAmount);
        var todayRevenue = todayOrders.Sum(o => o.TotalAmount);
        var revenueChange = yRevenue <= 0
            ? (todayRevenue > 0 ? 100m : 0m)
            : decimal.Round((todayRevenue - yRevenue) / yRevenue * 100m, 1);

        var unpaid = allArea
            .Where(o =>
                o.Status != OrderStatus.Cancelled
                && o.PaymentStatus is PaymentStatus.Pending
                    or PaymentStatus.Partial
                    or PaymentStatus.Overdue)
            .ToList();
        var unpaidAmount = unpaid.Sum(o => Math.Max(0, o.TotalAmount - o.PaidAmount));
        var unpaidToday = unpaid.Count(o => o.OrderDate >= today && o.OrderDate < tomorrow);
        var unpaidYesterday = unpaid.Count(o => o.OrderDate >= yesterday && o.OrderDate < today);
        var ordersToday = allArea.Count(o => o.OrderDate >= today && o.OrderDate < tomorrow);

        var inventory = await GetInventoryAsync(farmingAreaId, ct);
        var items = inventory.Data?.ToList() ?? [];
        return ApiResponse<SalesOverviewDto>.Ok(new SalesOverviewDto(
            todayRevenue,
            soldToday,
            items.Count,
            decimal.Round(items.Sum(i => i.WeightGram ?? 0) / 1000m, 3),
            ordersToday,
            decimal.Round(unpaidAmount, 0),
            revenueChange,
            soldToday - soldYesterday,
            unpaidToday - unpaidYesterday));
    }

    public async Task<ApiResponse<IEnumerable<InventoryCrabDto>>> GetInventoryAsync(
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var reserved = await ReservedCrabIdsAsync(null, ct);
        var harvested = (await _uow.Crabs.FindAsync(c => c.Status == CrabStatus.Harvested, ct))
            .Where(c => !reserved.Contains(c.Id))
            .ToList();
        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var areaVoucherIds = (await _uow.HarvestVouchers.FindAsync(
                    v => v.FarmingAreaId == areaId && v.Status == HarvestStatus.Completed, ct))
                .Select(v => v.Id)
                .ToHashSet();
            var crabIds = areaVoucherIds.Count == 0
                ? new HashSet<Guid>()
                : (await _uow.HarvestLines.FindAsync(
                        l => l.CrabId != null && areaVoucherIds.Contains(l.HarvestVoucherId),
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
        var harvestedIds = harvested.Select(c => c.Id).ToHashSet();
        var harvestLines = harvestedIds.Count == 0
            ? []
            : (await _uow.HarvestLines.FindAsync(
                    l => l.CrabId != null && harvestedIds.Contains(l.CrabId.Value), ct))
                .ToList();
        var voucherIds = harvestLines.Select(l => l.HarvestVoucherId).Distinct().ToHashSet();
        var vouchers = voucherIds.Count == 0
            ? new Dictionary<Guid, HarvestVoucher>()
            : (await _uow.HarvestVouchers.FindAsync(v => voucherIds.Contains(v.Id), ct))
                .ToDictionary(v => v.Id);
        var harvestByCrab = harvestLines
            .GroupBy(l => l.CrabId!.Value)
            .ToDictionary(g => g.Key, g => g.Last());

        var rows = harvested
            .OrderByDescending(c => latest.GetValueOrDefault(c.Id)?.HarvestedAt ?? c.UpdatedAt)
            .Select(c =>
            {
                latest.TryGetValue(c.Id, out var hist);
                harvestByCrab.TryGetValue(c.Id, out var hLine);
                string? boxCode = hLine?.BoxCode;
                if (string.IsNullOrWhiteSpace(boxCode) && c.BoxId is Guid bid)
                    boxes.TryGetValue(bid, out boxCode);
                var eligibility = c.Condition is CrabCondition.Problem or CrabCondition.Dead
                    ? "REVIEW"
                    : "READY";
                var voucherCode = hLine != null && vouchers.TryGetValue(hLine.HarvestVoucherId, out var v)
                    ? v.VoucherCode
                    : null;
                return new InventoryCrabDto(
                    c.Id,
                    c.Code,
                    boxCode,
                    c.WeightGram ?? hist?.WeightGram,
                    hist?.Grade ?? hLine?.Grade,
                    c.Condition.ToString(),
                    c.CrabType,
                    hist?.HarvestedAt,
                    voucherCode,
                    hLine?.LotCode,
                    eligibility,
                    c.Condition == CrabCondition.Softshell || (hLine?.IsSoftshell ?? false));
            })
            .ToList();
        return ApiResponse<IEnumerable<InventoryCrabDto>>.Ok(rows);
    }

    private async Task<(SalesOrderLine Line, decimal Amount)> BuildLineAsync(
        Guid orderId,
        CreateSalesOrderLineRequest lineReq,
        CancellationToken ct)
    {
        var crab = await _uow.Crabs.GetByIdAsync(lineReq.CrabId, ct)
            ?? throw AppException.BadRequest($"Cua '{lineReq.CrabId}' không tồn tại.");
        if (crab.Status != CrabStatus.Harvested)
            throw AppException.Conflict(
                $"Cua '{crab.Code}' chưa ở tồn kho (Đã thu hoạch). Không bán cua đang nuôi.");

        var reserved = await ReservedCrabIdsAsync(null, ct);
        if (reserved.Contains(crab.Id))
            throw AppException.Conflict($"Cua '{crab.Code}' đã nằm trong đơn khác.");

        var weight = lineReq.WeightGram ?? crab.WeightGram ?? 0;
        if (weight <= 0)
            throw AppException.BadRequest($"Cua '{crab.Code}' thiếu trọng lượng.");
        var price = lineReq.UnitPricePerKg ?? 0;
        if (price < 0)
            throw AppException.BadRequest("Đơn giá phải >= 0.");

        var qtyKg = decimal.Round(weight / 1000m, 3, MidpointRounding.AwayFromZero);
        var amount = decimal.Round(qtyKg * price, 0, MidpointRounding.AwayFromZero);
        var line = new SalesOrderLine
        {
            SalesOrderId = orderId,
            CrabId = crab.Id,
            CrabCode = crab.Code,
            CrabType = string.IsNullOrWhiteSpace(crab.CrabType) ? null : crab.CrabType.Trim(),
            Grade = string.IsNullOrWhiteSpace(lineReq.Grade) ? null : lineReq.Grade.Trim(),
            WeightGram = weight,
            Quantity = 1,
            QuantityKg = qtyKg,
            UnitPricePerKg = price,
            TotalAmount = amount
        };
        return (line, amount);
    }

    private async Task MarkLinesSoldAsync(SalesOrder order, CancellationToken ct)
    {
        foreach (var line in order.Lines)
        {
            if (line.CrabId is not Guid crabId) continue;
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
                ?? throw AppException.BadRequest($"Cua '{line.CrabCode}' không tồn tại.");
            if (crab.Status == CrabStatus.Sold) continue;
            if (crab.Status != CrabStatus.Harvested)
                throw AppException.Conflict(
                    $"Cua '{crab.Code}' không còn trong kho để xác nhận bán.");

            var oldStatus = crab.Status;
            var oldCondition = crab.Condition;
            crab.Status = CrabStatus.Sold;
            crab.Condition = CrabCondition.Sold;
            if (line.WeightGram is decimal w && w > 0)
                crab.WeightGram = w;
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
    }

    private async Task RestoreLinesToInventoryAsync(SalesOrder order, CancellationToken ct)
    {
        foreach (var line in order.Lines)
        {
            if (line.CrabId is not Guid crabId) continue;
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
            if (crab is null || crab.Status != CrabStatus.Sold) continue;

            var oldStatus = crab.Status;
            var oldCondition = crab.Condition;
            crab.Status = CrabStatus.Harvested;
            crab.Condition = CrabCondition.Harvested;
            _uow.Crabs.Update(crab);

            await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
            {
                CrabId = crab.Id,
                OldStatus = oldStatus,
                NewStatus = CrabStatus.Harvested,
                OldCondition = oldCondition,
                NewCondition = CrabCondition.Harvested,
                ChangedAt = DateTime.UtcNow,
                Source = "sale-cancel",
                Reason = order.OrderCode,
                ChangedByUserId = _currentUser.UserId
            }, ct);
        }
    }

    private async Task<HashSet<Guid>> ReservedCrabIdsAsync(Guid? exceptOrderId, CancellationToken ct)
    {
        var activeIds = (await _uow.SalesOrders.GetAllAsync(ct))
            .Where(o =>
                o.Status != OrderStatus.Cancelled
                && (exceptOrderId is null || o.Id != exceptOrderId))
            .Select(o => o.Id)
            .ToHashSet();
        if (activeIds.Count == 0) return [];
        return (await _uow.SalesOrderLines.FindAsync(
                l => l.CrabId != null && activeIds.Contains(l.SalesOrderId), ct))
            .Select(l => l.CrabId!.Value)
            .ToHashSet();
    }

    private async Task LoadLinesAsync(SalesOrder order, CancellationToken ct)
    {
        if (order.Lines.Count > 0) return;
        foreach (var line in await _uow.SalesOrderLines.FindAsync(l => l.SalesOrderId == order.Id, ct))
            order.Lines.Add(line);
    }

    private static void ApplyTotals(SalesOrder order, decimal subtotal, decimal? paidAmount)
    {
        var grand = decimal.Round(
            Math.Max(0, subtotal - order.DiscountAmount + order.ShippingFee),
            0,
            MidpointRounding.AwayFromZero);
        order.SubtotalAmount = subtotal;
        order.TotalAmount = grand;
        order.PaidAmount = order.PaymentStatus switch
        {
            PaymentStatus.Paid => grand,
            PaymentStatus.Partial => decimal.Round(
                Math.Clamp(paidAmount ?? 0, 0, grand), 0, MidpointRounding.AwayFromZero),
            _ => 0
        };
        if (order.PaymentStatus == PaymentStatus.Partial && order.PaidAmount <= 0)
            throw AppException.BadRequest("Thanh toán một phần cần số tiền đã thu > 0.");
        if (order.PaymentStatus == PaymentStatus.Partial && order.PaidAmount >= grand)
            order.PaymentStatus = PaymentStatus.Paid;
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
            if (!string.IsNullOrWhiteSpace(customerType)) existing.CustomerType = customerType.Trim();
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
        var weightKg = decimal.Round(lines.Sum(l =>
            l.QuantityKg > 0
                ? l.QuantityKg
                : (l.WeightGram ?? 0) / 1000m), 3);
        return new SalesOrderDto(
            order.Id,
            order.OrderCode,
            order.OrderDate,
            order.Status.ToString(),
            order.PaymentStatus.ToString(),
            order.PaymentMethod,
            order.DeliveryStatus,
            order.SubtotalAmount,
            order.DiscountAmount,
            order.ShippingFee,
            order.TotalAmount,
            order.PaidAmount,
            weightKg,
            lines.Sum(l => l.Quantity <= 0 ? 1 : l.Quantity),
            order.CustomerId,
            customer?.Name ?? "",
            customer?.Phone,
            customer?.Address,
            order.SellerName,
            order.FarmingAreaId,
            order.Notes,
            await MapLinesAsync(lines, ct),
            customer?.CustomerType);
    }

    private async Task<IReadOnlyList<SalesOrderLineDto>> MapLinesAsync(
        IEnumerable<SalesOrderLine> lines,
        CancellationToken ct)
    {
        var list = lines.ToList();
        var crabIds = list.Where(l => l.CrabId.HasValue).Select(l => l.CrabId!.Value).ToHashSet();
        var harvestLines = crabIds.Count == 0
            ? []
            : (await _uow.HarvestLines.FindAsync(
                    l => l.CrabId != null && crabIds.Contains(l.CrabId.Value), ct))
                .ToList();
        var voucherIds = harvestLines.Select(l => l.HarvestVoucherId).Distinct().ToHashSet();
        var vouchers = voucherIds.Count == 0
            ? new Dictionary<Guid, HarvestVoucher>()
            : (await _uow.HarvestVouchers.FindAsync(v => voucherIds.Contains(v.Id), ct))
                .ToDictionary(v => v.Id);
        var byCrab = harvestLines
            .GroupBy(l => l.CrabId!.Value)
            .ToDictionary(g => g.Key, g => g.Last());

        return list.Select(l =>
        {
            string? slip = null;
            string? box = null;
            string? lot = null;
            if (l.CrabId is Guid cid && byCrab.TryGetValue(cid, out var found))
            {
                if (vouchers.TryGetValue(found.HarvestVoucherId, out var v))
                    slip = v.VoucherCode;
                box = found.BoxCode;
                lot = found.LotCode;
            }
            return new SalesOrderLineDto(
                l.Id,
                l.CrabId,
                l.CrabCode,
                l.CrabType,
                l.Grade,
                l.Quantity <= 0 ? 1 : l.Quantity,
                l.WeightGram ?? decimal.Round(l.QuantityKg * 1000m, 1),
                l.QuantityKg,
                l.UnitPricePerKg,
                l.TotalAmount,
                slip,
                box,
                lot);
        }).ToList();
    }

    private static CustomerDto MapCustomer(Customer c) =>
        new(c.Id, c.Name, c.Phone, c.Email, c.Address, c.CustomerType, c.IsActive);

    private static OrderStatus ParseOrderStatus(string? raw)
    {
        var key = (raw ?? "completed").Trim().ToLowerInvariant();
        return key switch
        {
            "draft" or "nhap" or "nháp" or "quotation" => OrderStatus.Draft,
            "cancelled" or "canceled" or "huy" or "đã hủy" => OrderStatus.Cancelled,
            _ => OrderStatus.Completed
        };
    }

    private static PaymentStatus ParsePayment(string? raw, string? method)
    {
        var methodKey = (method ?? "").Trim().ToLowerInvariant();
        if (methodKey is "unpaid" or "chua" or "chưa thanh toán")
            return PaymentStatus.Pending;
        if (string.IsNullOrWhiteSpace(raw)) return PaymentStatus.Pending;
        var key = raw.Trim().ToLowerInvariant();
        return key switch
        {
            "paid" or "dathanhtoan" or "đã thanh toán" => PaymentStatus.Paid,
            "partial" or "motphan" or "một phần" or "thanh toán một phần" => PaymentStatus.Partial,
            "overdue" => PaymentStatus.Overdue,
            _ => PaymentStatus.Pending
        };
    }

    private static string? NormalizePaymentMethod(string? raw, PaymentStatus payment)
    {
        if (payment == PaymentStatus.Pending) return raw is null ? null : "unpaid";
        var key = (raw ?? "cash").Trim().ToLowerInvariant();
        return key switch
        {
            "transfer" or "bank" or "chuyển khoản" or "chuyenkhoan" => "transfer",
            "credit" or "congno" or "công nợ" or "debt" => "credit",
            "other" or "khác" or "khac" => "other",
            "unpaid" or "chua" => "unpaid",
            _ => "cash"
        };
    }

    private static string? NormalizeDelivery(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "pickup";
        var key = raw.Trim().ToLowerInvariant();
        return key switch
        {
            "delivery" or "giao" or "giao hàng" or "giao tận nơi" => "delivery",
            "self" or "self_transport" or "tự vận chuyển" or "tu van chuyen" => "self",
            "shipping" or "danggiao" or "đang giao" => "shipping",
            "delivered" or "dagiao" or "đã giao" => "delivered",
            _ => "pickup"
        };
    }

    private async Task<string> GenerateOrderCodeAsync(CancellationToken ct)
    {
        var codes = (await _uow.SalesOrders.GetAllAsync(ct))
            .Select(o => o.OrderCode)
            .Where(c => c.StartsWith("SALE-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var max = 0;
        foreach (var code in codes)
        {
            var tail = code["SALE-".Length..];
            if (int.TryParse(tail, out var n) && n > max) max = n;
        }

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var next = $"SALE-{(max + attempt):000}";
            if (!await _uow.SalesOrders.AnyAsync(o => o.OrderCode == next, ct))
                return next;
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
