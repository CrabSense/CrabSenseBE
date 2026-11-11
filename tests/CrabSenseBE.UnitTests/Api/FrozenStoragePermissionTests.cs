using System.Text;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class FrozenStoragePermissionTests : IAsyncLifetime
{
    private static readonly Guid AreaA = Guid.Parse("99999999-0000-0000-0000-00000000000a");
    private static readonly Guid AreaB = Guid.Parse("99999999-0000-0000-0000-00000000000b");
    private static readonly Guid LotA = Guid.Parse("99999999-0000-0000-0000-0000000000a1");
    private static readonly Guid LotB = Guid.Parse("99999999-0000-0000-0000-0000000000b1");
    private static readonly Guid ManagerA = Guid.Parse("99999999-0000-0000-0000-0000000000f1");
    private static readonly Guid StaffA = Guid.Parse("99999999-0000-0000-0000-0000000000f2");
    private static readonly Guid SystemId = Guid.Parse("99999999-0000-0000-0000-0000000000f3");
    private static readonly Guid AdminA = Guid.Parse("99999999-0000-0000-0000-0000000000f4");

    private readonly List<FrozenStorageTransaction> _transactions = new();
    private readonly List<FrozenInventoryAdjustment> _adjustments = new();
    private readonly List<FrozenInventoryOpeningBalance> _openingBalances = new();
    private FrozenLot _lotA = null!;
    private int _saves;
    private Exception? _saveFailure;
    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var manager = new AppUser
        {
            Id = ManagerA, Role = UserRole.FarmManager, Username = "fm-a", IsActive = true
        };
        var staff = new AppUser
        {
            Id = StaffA, Role = UserRole.FarmStaff, Username = "fs-a", IsActive = true
        };
        var admin = new AppUser
        {
            Id = AdminA, Role = UserRole.FarmAdministrator, Username = "fa-a", IsActive = true
        };
        var users = new Dictionary<Guid, AppUser>
        {
            [ManagerA] = manager, [StaffA] = staff, [AdminA] = admin
        };
        var areaA = new FarmingArea
        {
            Id = AreaA, Code = "AREA-A", Name = "Khu A", Administrators = { admin }
        };
        var areaB = new FarmingArea { Id = AreaB, Code = "AREA-B", Name = "Khu B" };
        _lotA = new FrozenLot
        {
            Id = LotA, LotCode = "FZ-A", FarmingAreaId = AreaA,
            FrozenDate = DateTime.UtcNow, ExpiryDate = DateTime.UtcNow.AddMonths(6)
        };
        var lotB = new FrozenLot
        {
            Id = LotB, LotCode = "FZ-B", FarmingAreaId = AreaB,
            FrozenDate = DateTime.UtcNow, ExpiryDate = DateTime.UtcNow.AddMonths(6)
        };

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => users.GetValueOrDefault(id));
        var managers = new Mock<IRepository<FarmManagerAssignment>>();
        managers.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FarmManagerAssignment { ManagerUserId = ManagerA, FarmingAreaId = AreaA }
            });
        var staffAssignments = new Mock<IRepository<FarmStaffAssignment>>();
        staffAssignments.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = StaffA, ManagerUserId = ManagerA, FarmingAreaId = AreaA
            });

        var transactionRepo = Repo(_transactions, item => item.Id);
        transactionRepo.Setup(r => r.AddAsync(
                It.IsAny<FrozenStorageTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<FrozenStorageTransaction, CancellationToken>((item, _) => _transactions.Add(item))
            .Returns(Task.CompletedTask);
        transactionRepo.Setup(r => r.Update(It.IsAny<FrozenStorageTransaction>()));

        var adjustmentRepo = Repo(_adjustments, item => item.Id);
        adjustmentRepo.Setup(r => r.AddAsync(
                It.IsAny<FrozenInventoryAdjustment>(), It.IsAny<CancellationToken>()))
            .Callback<FrozenInventoryAdjustment, CancellationToken>((item, _) => _adjustments.Add(item))
            .Returns(Task.CompletedTask);
        adjustmentRepo.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FrozenInventoryAdjustment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<FrozenInventoryAdjustment, bool>> predicate,
                CancellationToken _) => _adjustments.Any(predicate.Compile()));
        adjustmentRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FrozenInventoryAdjustment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<FrozenInventoryAdjustment, bool>> predicate,
                CancellationToken _) => _adjustments.Where(predicate.Compile()).ToList());
        var openingRepo = Repo(_openingBalances, item => item.Id);
        openingRepo.Setup(r => r.Update(It.IsAny<FrozenInventoryOpeningBalance>()));
        _openingBalances.Add(new FrozenInventoryOpeningBalance
        {
            Id = Guid.NewGuid(),
            FrozenLotId = LotA,
            FarmingAreaId = AreaA,
            OpeningQuantity = 0,
            OpeningWeightKg = 0,
            OpeningDate = DateTime.UtcNow,
            Source = "LegacyFrozenLotReconciliation",
            VerificationStatus = FrozenOpeningBalanceVerificationStatus.PendingReview
        });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Users).Returns(userRepo.Object);
        uow.Setup(x => x.FarmManagerAssignments).Returns(managers.Object);
        uow.Setup(x => x.FarmStaffAssignments).Returns(staffAssignments.Object);
        var areaRepo = Repo(new List<FarmingArea> { areaA, areaB }, item => item.Id);
        areaRepo.Setup(repo => repo.Query())
            .Returns(() => new[] { areaA, areaB }.AsQueryable());
        uow.Setup(x => x.FarmingAreas).Returns(areaRepo.Object);
        uow.Setup(x => x.FrozenLots).Returns(Repo(new List<FrozenLot> { _lotA, lotB }, item => item.Id).Object);
        uow.Setup(x => x.FrozenStorageTransactions).Returns(transactionRepo.Object);
        uow.Setup(x => x.FrozenInventoryAdjustments).Returns(adjustmentRepo.Object);
        uow.Setup(x => x.FrozenInventoryOpeningBalances).Returns(openingRepo.Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) =>
            {
                _saves++;
                if (_saveFailure is not null)
                    throw _saveFailure;
                return Task.FromResult(1);
            });

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(uow.Object);
            services.AddScoped<IFrozenStorageService, FrozenStorageService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Staff_creates_but_cannot_approve_manager_cannot_execute_and_other_area_is_denied()
    {
        var failures = new List<string>();
        var inboundA =
            $"{{\"frozenLotId\":\"{LotA}\",\"type\":\"Inbound\",\"requestedQuantity\":5,\"requestedWeightKg\":1.25}}";
        var inboundB =
            $"{{\"frozenLotId\":\"{LotB}\",\"type\":\"Inbound\",\"requestedQuantity\":2,\"requestedWeightKg\":0.5}}";

        await Expect(failures, "FA xem giao dịch", 200, AppRoles.FarmAdministrator, AdminA,
            HttpMethod.Get, "/api/frozen-storage-transactions", "{}");
        await Expect(failures, "FA lập phiếu", 403, AppRoles.FarmAdministrator, AdminA,
            HttpMethod.Post, "/api/frozen-storage-transactions", inboundA);
        await Expect(failures, "SA xem giao dịch", 403, AppRoles.SystemAdministrator, SystemId,
            HttpMethod.Get, "/api/frozen-storage-transactions", "{}");
        await Expect(failures, "FS lập phiếu", 200, AppRoles.FarmStaff, StaffA,
            HttpMethod.Post, "/api/frozen-storage-transactions", inboundA);
        var transaction = _transactions.SingleOrDefault();
        if (transaction is null)
            failures.Add("FS lập phiếu nhưng không lưu phiếu chờ duyệt.");
        else
        {
            await Expect(failures, "FS tự duyệt", 403, AppRoles.FarmStaff, StaffA,
                HttpMethod.Post, $"/api/frozen-storage-transactions/{transaction.Id}/approve", "{}");
            await Expect(failures, "FM duyệt", 200, AppRoles.FarmManager, ManagerA,
                HttpMethod.Post, $"/api/frozen-storage-transactions/{transaction.Id}/approve", "{}");
            await Expect(failures, "FM tự thực hiện", 403, AppRoles.FarmManager, ManagerA,
                HttpMethod.Post, $"/api/frozen-storage-transactions/{transaction.Id}/execute",
                "{\"actualQuantity\":5,\"actualWeightKg\":1.2}");
            await Expect(failures, "FS thực hiện", 200, AppRoles.FarmStaff, StaffA,
                HttpMethod.Post, $"/api/frozen-storage-transactions/{transaction.Id}/execute",
                "{\"actualQuantity\":5,\"actualWeightKg\":1.2," +
                "\"varianceReason\":\"Sai số cân thực tế\"}");
            if (_lotA.Quantity != 5 || _lotA.WeightKg != 1.2m || _adjustments.Count != 1)
                failures.Add("Thực hiện nhập kho không cập nhật đúng số thực tế hoặc không ghi nhật ký.");
            if (transaction.ApprovedQuantity != 5
                || transaction.ApprovedWeightKg != 1.25m
                || transaction.QuantityVariance != 0
                || transaction.WeightVarianceKg != -0.05m
                || transaction.VarianceReason != "Sai số cân thực tế")
                failures.Add("Phiếu không giữ đúng snapshot duyệt hoặc chênh lệch thực tế.");

            await Expect(failures, "FS thực hiện lặp", 409, AppRoles.FarmStaff, StaffA,
                HttpMethod.Post, $"/api/frozen-storage-transactions/{transaction.Id}/execute",
                "{\"actualQuantity\":5,\"actualWeightKg\":1.2}");
            if (_lotA.Quantity != 5 || _lotA.WeightKg != 1.2m || _adjustments.Count != 1)
                failures.Add("Thực hiện lặp đã cộng tồn kho lần thứ hai.");
        }

        var pending = new FrozenStorageTransaction
        {
            Id = Guid.NewGuid(), FrozenLotId = LotA, FarmingAreaId = AreaA,
            Type = FrozenStorageTransactionType.Inbound,
            Status = FrozenStorageTransactionStatus.PendingApproval,
            RequestedQuantity = 2, RequestedWeightKg = 0.5m,
            CreatedByUserId = StaffA
        };
        _transactions.Add(pending);
        var quantityBeforeReject = _lotA.Quantity;
        var weightBeforeReject = _lotA.WeightKg;
        var adjustmentsBeforeReject = _adjustments.Count;
        await Expect(failures, "FS thực hiện phiếu chưa duyệt", 409, AppRoles.FarmStaff, StaffA,
            HttpMethod.Post, $"/api/frozen-storage-transactions/{pending.Id}/execute",
            "{\"actualQuantity\":2,\"actualWeightKg\":0.5}");
        await Expect(failures, "FM từ chối phiếu", 200, AppRoles.FarmManager, ManagerA,
            HttpMethod.Post, $"/api/frozen-storage-transactions/{pending.Id}/reject",
            "{\"reason\":\"Không đạt yêu cầu\"}");
        if (pending.Status != FrozenStorageTransactionStatus.Rejected
            || _lotA.Quantity != quantityBeforeReject
            || _lotA.WeightKg != weightBeforeReject
            || _adjustments.Count != adjustmentsBeforeReject)
            failures.Add("Từ chối phiếu đã làm thay đổi tồn kho hoặc không lưu trạng thái Rejected.");
        await Expect(failures, "FS thực hiện phiếu bị từ chối", 409, AppRoles.FarmStaff, StaffA,
            HttpMethod.Post, $"/api/frozen-storage-transactions/{pending.Id}/execute",
            "{\"actualQuantity\":2,\"actualWeightKg\":0.5}");

        var selfApproval = new FrozenStorageTransaction
        {
            Id = Guid.NewGuid(), FrozenLotId = LotA, FarmingAreaId = AreaA,
            Type = FrozenStorageTransactionType.Inbound,
            Status = FrozenStorageTransactionStatus.PendingApproval,
            RequestedQuantity = 1, RequestedWeightKg = 0.2m,
            CreatedByUserId = ManagerA
        };
        _transactions.Add(selfApproval);
        var savesBeforeSelfApproval = _saves;
        await Expect(failures, "FM tự duyệt phiếu mình lập", 403, AppRoles.FarmManager, ManagerA,
            HttpMethod.Post, $"/api/frozen-storage-transactions/{selfApproval.Id}/approve", "{}");
        if (selfApproval.Status != FrozenStorageTransactionStatus.PendingApproval
            || _saves != savesBeforeSelfApproval)
            failures.Add("Phiếu kho bị thay đổi dù yêu cầu tự duyệt bị từ chối.");

        var foreign = new FrozenStorageTransaction
        {
            Id = Guid.NewGuid(), FrozenLotId = LotB, FarmingAreaId = AreaB,
            Type = FrozenStorageTransactionType.Inbound,
            Status = FrozenStorageTransactionStatus.PendingApproval,
            RequestedQuantity = 1, RequestedWeightKg = 0.2m,
            CreatedByUserId = Guid.NewGuid()
        };
        _transactions.Add(foreign);
        var savesBeforeForeign = _saves;
        await Expect(failures, "FM duyệt phiếu ngoài khu", 403, AppRoles.FarmManager, ManagerA,
            HttpMethod.Post, $"/api/frozen-storage-transactions/{foreign.Id}/approve", "{}");
        if (foreign.Status != FrozenStorageTransactionStatus.PendingApproval
            || _saves != savesBeforeForeign)
            failures.Add("Phiếu ngoài khu bị thay đổi dù request bị từ chối.");

        await Expect(failures, "FS lập phiếu ngoài khu", 403, AppRoles.FarmStaff, StaffA,
            HttpMethod.Post, "/api/frozen-storage-transactions", inboundB);
        await Expect(failures, "SA ghi kho", 403, AppRoles.SystemAdministrator, SystemId,
            HttpMethod.Post, "/api/frozen-storage-transactions", inboundA);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Execution_respects_approved_limits_stock_and_variance_policy()
    {
        var failures = new List<string>();
        var exact = ApprovedTransaction(
            FrozenStorageTransactionType.Inbound, 10, 2m);
        _transactions.Add(exact);

        await Expect(failures, "Thực hiện đúng mức duyệt", 200,
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/frozen-storage-transactions/{exact.Id}/execute",
            "{\"actualQuantity\":10,\"actualWeightKg\":2}");
        if (_lotA.Quantity != 10 || _lotA.WeightKg != 2m
            || exact.RequestedQuantity != 10 || exact.RequestedWeightKg != 2m)
            failures.Add("Thực hiện đúng mức không cập nhật tồn hoặc đã sửa dữ liệu đề nghị.");

        var over = ApprovedTransaction(
            FrozenStorageTransactionType.Inbound, 10, 2m);
        _transactions.Add(over);
        var adjustmentCount = _adjustments.Count;
        await Expect(failures, "Thực hiện vượt mức duyệt", 409,
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/frozen-storage-transactions/{over.Id}/execute",
            "{\"actualQuantity\":20,\"actualWeightKg\":4}");
        if (_lotA.Quantity != 10 || _lotA.WeightKg != 2m
            || _adjustments.Count != adjustmentCount
            || over.Status != FrozenStorageTransactionStatus.Approved)
            failures.Add("Request vượt mức duyệt đã làm thay đổi dữ liệu.");

        var under = ApprovedTransaction(
            FrozenStorageTransactionType.Inbound, 10, 2m);
        _transactions.Add(under);
        await Expect(failures, "Thiếu thực tế nhưng không ghi lý do", 400,
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/frozen-storage-transactions/{under.Id}/execute",
            "{\"actualQuantity\":8,\"actualWeightKg\":1.5}");
        if (_lotA.Quantity != 10 || _lotA.WeightKg != 2m
            || _adjustments.Count != adjustmentCount)
            failures.Add("Request thiếu lý do chênh lệch đã làm thay đổi dữ liệu.");

        await Expect(failures, "Thiếu thực tế có lý do", 200,
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/frozen-storage-transactions/{under.Id}/execute",
            "{\"actualQuantity\":8,\"actualWeightKg\":1.5," +
            "\"varianceReason\":\"Hàng lỗi khi kiểm đếm\"}");
        if (_lotA.Quantity != 18 || _lotA.WeightKg != 3.5m
            || under.QuantityVariance != -2 || under.WeightVarianceKg != -0.5m
            || under.VarianceReason != "Hàng lỗi khi kiểm đếm"
            || _adjustments.Count != adjustmentCount + 1)
            failures.Add("Chênh lệch thực tế hợp lệ không được lưu nhất quán.");

        var insufficient = ApprovedTransaction(
            FrozenStorageTransactionType.Outbound, 20, 4m);
        _transactions.Add(insufficient);
        adjustmentCount = _adjustments.Count;
        await Expect(failures, "Xuất vượt tồn", 409,
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/frozen-storage-transactions/{insufficient.Id}/execute",
            "{\"actualQuantity\":20,\"actualWeightKg\":4}");
        if (_lotA.Quantity != 18 || _lotA.WeightKg != 3.5m
            || _adjustments.Count != adjustmentCount
            || insufficient.Status != FrozenStorageTransactionStatus.Approved)
            failures.Add("Request xuất vượt tồn đã làm thay đổi dữ liệu.");

        var mismatchedArea = ApprovedTransaction(
            FrozenStorageTransactionType.Inbound, 1, 0.1m);
        mismatchedArea.FrozenLotId = LotB;
        _transactions.Add(mismatchedArea);
        await Expect(failures, "Phiếu và lô khác khu", 403,
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/frozen-storage-transactions/{mismatchedArea.Id}/execute",
            "{\"actualQuantity\":1,\"actualWeightKg\":0.1}");
        if (_lotA.Quantity != 18 || _lotA.WeightKg != 3.5m
            || _adjustments.Count != adjustmentCount)
            failures.Add("Request sai khu đã làm thay đổi dữ liệu.");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Concurrent_inventory_update_is_returned_as_conflict()
    {
        var transaction = ApprovedTransaction(
            FrozenStorageTransactionType.Inbound, 1, 0.1m);
        _transactions.Add(transaction);
        _saveFailure = new DbUpdateConcurrencyException("simulated concurrent update");
        var failures = new List<string>();

        await Expect(failures, "Xung đột optimistic concurrency", 409,
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/frozen-storage-transactions/{transaction.Id}/execute",
            "{\"actualQuantity\":1,\"actualWeightKg\":0.1}");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task Only_manager_can_verify_opening_balance_without_changing_inventory()
    {
        var balance = _openingBalances.Single();
        var failures = new List<string>();
        const string payload =
            "{\"openingQuantity\":0,\"openingWeightKg\":0,\"reason\":\"Đã đối chiếu sổ kho\"}";

        await Expect(failures, "FS xác nhận số dư đầu kỳ", 403,
            AppRoles.FarmStaff, StaffA, HttpMethod.Post,
            $"/api/frozen-opening-balances/{balance.Id}/verify", payload);
        await Expect(failures, "SA xác nhận số dư đầu kỳ", 403,
            AppRoles.SystemAdministrator, SystemId, HttpMethod.Post,
            $"/api/frozen-opening-balances/{balance.Id}/verify", payload);
        await Expect(failures, "FM xác nhận số dư đầu kỳ", 200,
            AppRoles.FarmManager, ManagerA, HttpMethod.Post,
            $"/api/frozen-opening-balances/{balance.Id}/verify", payload);

        if (balance.VerificationStatus != FrozenOpeningBalanceVerificationStatus.Verified
            || balance.VerifiedByUserId != ManagerA
            || balance.VerifiedAt is null
            || _lotA.Quantity != 0
            || _lotA.WeightKg != 0)
        {
            failures.Add("Xác nhận số dư đầu kỳ sai quyền hoặc làm thay đổi tồn hiện tại.");
        }

        await Expect(failures, "FM xác nhận số dư lần hai", 409,
            AppRoles.FarmManager, ManagerA, HttpMethod.Post,
            $"/api/frozen-opening-balances/{balance.Id}/verify", payload);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Frozen_lot_and_transaction_are_optimistic_concurrency_entities()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=metadata_only")
            .Options;
        using var context = new AppDbContext(options);

        Assert.True(context.Model.FindEntityType(typeof(FrozenLot))!
            .FindProperty(nameof(FrozenLot.ConcurrencyVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(FrozenStorageTransaction))!
            .FindProperty(nameof(FrozenStorageTransaction.ConcurrencyVersion))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(FrozenInventoryAdjustment))!
            .GetIndexes()
            .Single(index => index.Properties.Count == 1
                && index.Properties[0].Name
                    == nameof(FrozenInventoryAdjustment.FrozenStorageTransactionId))
            .IsUnique);
    }

    private static FrozenStorageTransaction ApprovedTransaction(
        FrozenStorageTransactionType type,
        int quantity,
        decimal weight) => new()
    {
        Id = Guid.NewGuid(),
        FrozenLotId = LotA,
        FarmingAreaId = AreaA,
        Type = type,
        Status = FrozenStorageTransactionStatus.Approved,
        RequestedQuantity = quantity,
        RequestedWeightKg = weight,
        ApprovedQuantity = quantity,
        ApprovedWeightKg = weight,
        CreatedByUserId = Guid.NewGuid(),
        ApprovedByUserId = ManagerA,
        ApprovedAt = DateTime.UtcNow
    };

    private static Mock<IRepository<T>> Repo<T>(IList<T> items, Func<T, Guid> id) where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => items.ToList());
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid key, CancellationToken _) => items.FirstOrDefault(item => id(item) == key));
        return repo;
    }

    private async Task Expect(
        List<string> failures,
        string name,
        int expected,
        string role,
        Guid userId,
        HttpMethod method,
        string path,
        string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _api.SendAsync(role, userId, method, path, content);
        if ((int)response.StatusCode != expected)
            failures.Add(
                $"{name}: mong đợi {expected}, nhận {(int)response.StatusCode}. " +
                await response.Content.ReadAsStringAsync());
    }
}
