using System.Text;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

/// <summary>
/// Bước 7 — FM tạo và kết thúc lô trong khu được giao.
/// FA và FS không tạo hoặc kết thúc. FM không sửa lô ngoài khu.
/// </summary>
public sealed class CrabLotPermissionTests : IAsyncLifetime
{
    private static readonly Guid AreaA = Guid.Parse("aaaaaaa7-0000-0000-0000-00000000000a");
    private static readonly Guid AreaB = Guid.Parse("bbbbbbb7-0000-0000-0000-00000000000b");
    private static readonly Guid LotB = Guid.Parse("bbbbbbb7-0000-0000-0000-0000000000b1");
    private static readonly Guid AdminA = Guid.Parse("aaaaaaa7-0000-0000-0000-0000000000f1");
    private static readonly Guid ManagerA = Guid.Parse("ccccccc7-0000-0000-0000-0000000000f3");
    private static readonly Guid ManagerAB = Guid.Parse("ccccccc7-0000-0000-0000-0000000000f4");
    private static readonly Guid StaffA = Guid.Parse("ddddddd7-0000-0000-0000-0000000000f4");
    private static readonly Guid SystemId = Guid.Parse("eeeeeee7-0000-0000-0000-0000000000f5");

    private readonly List<CrabLot> _lots = new();
    private readonly List<CrabLotArea> _links = new();
    private readonly List<Crab> _crabs = new();
    private int _saves;
    private CrabLot _lotB = null!;
    private CrabLot _closedEmptyLot = null!;
    private PermissionApiFactory _api = null!;

    public Task InitializeAsync()
    {
        var admin = new AppUser { Id = AdminA, Role = UserRole.FarmAdministrator, IsActive = true, Username = "fa-a" };
        var manager = new AppUser { Id = ManagerA, Role = UserRole.FarmManager, IsActive = true, Username = "fm-a" };
        var managerAb = new AppUser { Id = ManagerAB, Role = UserRole.FarmManager, IsActive = true, Username = "fm-ab" };
        var staff = new AppUser { Id = StaffA, Role = UserRole.FarmStaff, IsActive = true, Username = "fs-a" };
        var users = new Dictionary<Guid, AppUser>
        {
            [AdminA] = admin, [ManagerA] = manager, [ManagerAB] = managerAb, [StaffA] = staff
        };
        var areaA = new FarmingArea
        {
            Id = AreaA, Code = "AREA-A01", Name = "Khu A", IsActive = true, Status = FarmStatus.Active,
            Administrators = { admin }
        };
        var areaB = new FarmingArea
        {
            Id = AreaB, Code = "AREA-B01", Name = "Khu B", IsActive = true, Status = FarmStatus.Active
        };
        _lotB = new CrabLot
        {
            Id = LotB, LotCode = "LOT-B", Name = "Lô ngoài khu", Quantity = 4, Status = "Pending"
        };
        _lots.Add(_lotB);
        _links.Add(new CrabLotArea { CrabLotId = LotB, FarmingAreaId = AreaB });
        _closedEmptyLot = new CrabLot
        {
            Id = Guid.NewGuid(),
            LotCode = "LOT-CLOSED",
            Name = "Lô đã kết thúc",
            Quantity = 0,
            Status = "Pending",
            StockedAt = DateTime.UtcNow.AddDays(-2),
            ClosedAt = DateTime.UtcNow.AddDays(-1)
        };
        _lots.Add(_closedEmptyLot);
        _links.Add(new CrabLotArea
        {
            CrabLotId = _closedEmptyLot.Id,
            FarmingAreaId = AreaA
        });

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => users.GetValueOrDefault(id));
        var managers = new Mock<IRepository<FarmManagerAssignment>>();
        var managerAssignments = new[]
        {
            new FarmManagerAssignment { ManagerUserId = ManagerA, FarmingAreaId = AreaA },
            new FarmManagerAssignment { ManagerUserId = ManagerAB, FarmingAreaId = AreaA },
            new FarmManagerAssignment { ManagerUserId = ManagerAB, FarmingAreaId = AreaB }
        };
        managers.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>> predicate,
                CancellationToken _) => managerAssignments.Where(predicate.Compile()).ToList());
        var staffRepo = new Mock<IRepository<FarmStaffAssignment>>();
        staffRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment { StaffUserId = StaffA, ManagerUserId = ManagerA, FarmingAreaId = AreaA });

        var lotRepo = Repo(_lots, lot => lot.Id);
        lotRepo.Setup(r => r.AddAsync(It.IsAny<CrabLot>(), It.IsAny<CancellationToken>()))
            .Callback<CrabLot, CancellationToken>((item, _) => _lots.Add(item))
            .Returns(Task.CompletedTask);
        lotRepo.Setup(r => r.Update(It.IsAny<CrabLot>()));
        lotRepo.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<CrabLot, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var linkRepo = Repo(_links, link => link.Id);
        linkRepo.Setup(r => r.AddAsync(It.IsAny<CrabLotArea>(), It.IsAny<CancellationToken>()))
            .Callback<CrabLotArea, CancellationToken>((item, _) => _links.Add(item))
            .Returns(Task.CompletedTask);

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _crabs.ToList());
        crabRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<Crab, bool>> predicate, CancellationToken _) =>
                _crabs.Where(predicate.Compile()).ToList());

        var areaRepo = Repo(new[] { areaA, areaB }, area => area.Id);
        areaRepo.Setup(r => r.Query()).Returns(() => new[] { areaA, areaB }.AsQueryable());

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Users).Returns(userRepo.Object);
        uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        uow.Setup(u => u.FarmManagerAssignments).Returns(managers.Object);
        uow.Setup(u => u.FarmStaffAssignments).Returns(staffRepo.Object);
        uow.Setup(u => u.CrabLots).Returns(lotRepo.Object);
        uow.Setup(u => u.CrabLotAreas).Returns(linkRepo.Object);
        uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        uow.Setup(u => u.MoltingRecords).Returns(
            Repo(new List<MoltingRecord>(), record => record.Id).Object);
        uow.Setup(u => u.FarmingRows).Returns(Repo(new List<FarmingRow>(), row => row.Id).Object);
        uow.Setup(u => u.Boxes).Returns(Repo(new List<Box>(), box => box.Id).Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _saves++)
            .ReturnsAsync(1);

        _api = PermissionApiFactory.Create(services =>
        {
            services.AddSingleton(uow.Object);
            services.AddSingleton(new Mock<IPublicImageStorage>().Object);
            services.AddScoped<IFarmLotService, FarmLotService>();
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Farm_manager_creates_and_closes_lot_in_assigned_area_only()
    {
        var failures = new List<string>();
        var createA = $"{{\"name\":\"Lô A\",\"quantity\":10,\"farmingAreaId\":\"{AreaA}\"}}";
        var createB = $"{{\"name\":\"Lô B mới\",\"quantity\":4,\"farmingAreaId\":\"{AreaB}\"}}";
        var createAB =
            $"{{\"name\":\"Lô A+B\",\"quantity\":6,\"farmingAreaIds\":[\"{AreaA}\",\"{AreaB}\"]}}";
        var rename = "{\"name\":\"Sửa trái khu\"}";

        var created = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Post, "/api/crab-lots", createA);
        var createdBody = await created.Content.ReadAsStringAsync();
        if ((int)created.StatusCode is not (200 or 201))
            failures.Add($"FM tạo lô đúng khu: mong đợi 200, nhận {(int)created.StatusCode}. {createdBody}");
        if (!createdBody.Contains("stockedAt", StringComparison.OrdinalIgnoreCase))
            failures.Add("Lô mới không lưu ngày bắt đầu thả cua.");
        await Expect(failures, "FM đủ quyền A+B tạo lô nhiều khu", 200,
            AppRoles.FarmManager, ManagerAB, HttpMethod.Post,
            "/api/crab-lots", createAB);

        var lotA = _lots.SingleOrDefault(lot => lot.Name == "Lô A");
        if (lotA is null || !_links.Any(link => link.CrabLotId == lotA.Id && link.FarmingAreaId == AreaA))
            failures.Add("Lô vừa tạo không gắn khu được phân công.");

        var savesAfterCreate = _saves;
        using (var faRead = await Send(
                   AppRoles.FarmAdministrator, AdminA, HttpMethod.Get, "/api/crab-lots", "{}"))
        {
            var body = await faRead.Content.ReadAsStringAsync();
            if ((int)faRead.StatusCode != 200 || body.Contains("LOT-B", StringComparison.OrdinalIgnoreCase))
                failures.Add("FA xem danh sách không được giới hạn theo khu A.");
        }
        using (var fsRead = await Send(
                   AppRoles.FarmStaff, StaffA, HttpMethod.Get, "/api/crab-lots", "{}"))
        {
            var body = await fsRead.Content.ReadAsStringAsync();
            if ((int)fsRead.StatusCode != 200 || body.Contains("LOT-B", StringComparison.OrdinalIgnoreCase))
                failures.Add("FS xem danh sách không được giới hạn theo khu A.");
        }
        await Expect(failures, "FA tạo lô", 403, AppRoles.FarmAdministrator, AdminA, HttpMethod.Post, "/api/crab-lots", createA);
        await Expect(failures, "FS tạo lô", 403, AppRoles.FarmStaff, StaffA, HttpMethod.Post, "/api/crab-lots", createA);
        await Expect(failures, "SA xem lô", 403, AppRoles.SystemAdministrator, SystemId, HttpMethod.Get, "/api/crab-lots", "{}");
        await Expect(failures, "SA tạo lô", 403, AppRoles.SystemAdministrator, SystemId, HttpMethod.Post, "/api/crab-lots", createA);
        await Expect(failures, "FM sửa lô ngoài khu", 403, AppRoles.FarmManager, ManagerA, HttpMethod.Put, $"/api/crab-lots/{LotB}", rename);
        if (_saves != savesAfterCreate)
            failures.Add("Request bị từ chối vẫn lưu lô.");
        if (_lotB.Name != "Lô ngoài khu")
            failures.Add("FM đã sửa được lô ngoài khu được giao.");
        if (_lots.Any(lot => lot.Name == "Lô B mới"))
            failures.Add("FM tạo được lô ở khu không được giao.");

        var savesBeforeClosedDelete = _saves;
        await Expect(failures, "FM xóa lô đã kết thúc", 409,
            AppRoles.FarmManager, ManagerA, HttpMethod.Delete,
            $"/api/crab-lots/{_closedEmptyLot.Id}", "{}");
        if (_saves != savesBeforeClosedDelete || !_lots.Contains(_closedEmptyLot))
            failures.Add("Request xóa lô đã kết thúc đã làm thay đổi dữ liệu lịch sử.");

        await Expect(failures, "FM tạo lô khu khác", 403, AppRoles.FarmManager, ManagerA, HttpMethod.Post, "/api/crab-lots", createB);

        if (lotA is not null)
        {
            var savesBeforeAddingArea = _saves;
            var originalName = lotA.Name;
            var addUnassignedArea =
                $"{{\"name\":\"Không được đổi\",\"farmingAreaIds\":[\"{AreaA}\",\"{AreaB}\"]}}";
            await Expect(failures, "FM thêm khu chưa được phân công vào lô", 403,
                AppRoles.FarmManager, ManagerA, HttpMethod.Put,
                $"/api/crab-lots/{lotA.Id}", addUnassignedArea);
            if (_saves != savesBeforeAddingArea
                || lotA.Name != originalName
                || _links.Any(link => link.CrabLotId == lotA.Id && link.FarmingAreaId == AreaB))
            {
                failures.Add("Request thêm khu chưa được phân công đã thay đổi dữ liệu lô.");
            }

            _crabs.Add(new Crab { CrabLotId = lotA.Id, Code = "CUA-1", Status = CrabStatus.Alive, MoltedAt = null });
            await Expect(failures, "FM kết thúc khi còn cua chưa lột mà không có phương án", 400,
                AppRoles.FarmManager, ManagerA, HttpMethod.Post, $"/api/crab-lots/{lotA.Id}/close", "{}");
            if (lotA.ClosedAt is not null)
                failures.Add("Lô bị ghi ngày kết thúc dù chưa có phương án xử lý.");

            var close = "{\"disposition\":\"Theo dõi tiếp đến khi lột\"}";
            var closed = await Send(AppRoles.FarmManager, ManagerA, HttpMethod.Post, $"/api/crab-lots/{lotA.Id}/close", close);
            var closedBody = await closed.Content.ReadAsStringAsync();
            if ((int)closed.StatusCode is not (200 or 201))
                failures.Add($"FM kết thúc lô đúng khu: mong đợi 200, nhận {(int)closed.StatusCode}. {closedBody}");
            if (lotA.ClosedAt is null || lotA.UnmoltedCountAtClose != 1 || lotA.CloseDisposition != "Theo dõi tiếp đến khi lột")
                failures.Add("Kết thúc lô không ghi ngày kết thúc, số cua chưa lột và phương án xử lý.");
            if (lotA.UpdatedAt == lotA.ClosedAt)
                failures.Add("Ngày kết thúc lô đang lấy từ UpdatedAt.");
            if (lotA.Status is "Completed")
                failures.Add("Kết thúc lô bị gán nhầm thành trạng thái phân bổ Completed.");

            var savesAfterClose = _saves;
            var closedName = lotA.Name;
            await Expect(failures, "FM kết thúc lô lần hai", 409,
                AppRoles.FarmManager, ManagerA, HttpMethod.Post, $"/api/crab-lots/{lotA.Id}/close", close);
            await Expect(failures, "FM sửa lô đã kết thúc", 409,
                AppRoles.FarmManager, ManagerA, HttpMethod.Put, $"/api/crab-lots/{lotA.Id}",
                "{\"name\":\"Không được đổi\"}");
            if (_saves != savesAfterClose || lotA.Name != closedName)
                failures.Add("Request trên lô đã kết thúc vẫn thay đổi dữ liệu.");

            await Expect(failures, "FA kết thúc lô", 403, AppRoles.FarmAdministrator, AdminA, HttpMethod.Post, $"/api/crab-lots/{lotA.Id}/close", close);
            await Expect(failures, "FS kết thúc lô", 403, AppRoles.FarmStaff, StaffA, HttpMethod.Post, $"/api/crab-lots/{lotA.Id}/close", close);

            _links.Add(new CrabLotArea { CrabLotId = lotA.Id, FarmingAreaId = AreaB });
            using var partialRead = await Send(
                AppRoles.FarmManager, ManagerA, HttpMethod.Get, $"/api/crab-lots/{lotA.Id}", "{}");
            var partialBody = await partialRead.Content.ReadAsStringAsync();
            if ((int)partialRead.StatusCode != 200
                || partialBody.Contains(AreaB.ToString(), StringComparison.OrdinalIgnoreCase)
                || !partialBody.Contains("\"isPartialScope\":true", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("READ lô nhiều khu làm lộ khu ngoài phạm vi hoặc không đánh dấu dữ liệu một phần.");
            }

            var savesBeforePartialManage = _saves;
            await Expect(failures, "FM chỉ có khu A sửa lô A+B", 403,
                AppRoles.FarmManager, ManagerA, HttpMethod.Put, $"/api/crab-lots/{lotA.Id}",
                "{\"name\":\"Không được sửa toàn lô\"}");
            await Expect(failures, "FM chỉ có khu A xóa lô A+B", 403,
                AppRoles.FarmManager, ManagerA, HttpMethod.Delete, $"/api/crab-lots/{lotA.Id}", "{}");
            await Expect(failures, "FM chỉ có khu A kết thúc lô A+B", 403,
                AppRoles.FarmManager, ManagerA, HttpMethod.Post, $"/api/crab-lots/{lotA.Id}/close", close);
            if (_saves != savesBeforePartialManage || lotA.Name != closedName)
                failures.Add("Request MANAGE lô nhiều khu bị từ chối vẫn thay đổi dữ liệu.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static Mock<IRepository<T>> Repo<T>(IList<T> items, Func<T, Guid> id) where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => items.ToList());
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid key, CancellationToken _) => items.FirstOrDefault(item => id(item) == key));
        return repo;
    }

    private async Task Expect(
        List<string> failures, string name, int expected,
        string role, Guid userId, HttpMethod method, string path, string json)
    {
        using var response = await Send(role, userId, method, path, json);
        var actual = (int)response.StatusCode;
        if (actual != expected)
            failures.Add($"{name}: mong đợi {expected}, nhận {actual}. {await response.Content.ReadAsStringAsync()}");
    }

    private Task<HttpResponseMessage> Send(string role, Guid userId, HttpMethod method, string path, string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return _api.SendAsync(role, userId, method, path, content);
    }
}
