using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class ScheduledTaskScopeTests
{
    [Fact]
    public async Task Staff_CannotCreateOutsideArea_AndDisabledHasNoNextRun()
    {
        var staff = Guid.NewGuid();
        var area = Guid.NewGuid();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.IsAuthenticated).Returns(true);
        current.SetupGet(c => c.UserId).Returns(staff);
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(r => r.GetByIdAsync(staff, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser { Id = staff, Role = UserRole.FarmStaff, IsActive = true });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment { StaffUserId = staff, FarmingAreaId = area });
        var tasks = new Mock<IRepository<ScheduledFarmTask>>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Users).Returns(users.Object);
        uow.Setup(u => u.FarmStaffAssignments).Returns(assignments.Object);
        uow.Setup(u => u.ScheduledFarmTasks).Returns(tasks.Object);
        var service = new ScheduledTaskService(uow.Object, currentUser: current.Object);
        var denied = await Assert.ThrowsAsync<AppException>(() => service.CreateAsync(staff,
            new CreateScheduledTaskRequest("other", FarmingAreaId: Guid.NewGuid())));
        Assert.Equal(403, denied.StatusCode);
        var result = await service.CreateAsync(staff, new CreateScheduledTaskRequest("disabled",
            FarmingAreaId: area, IsEnabled: false));
        Assert.Null(result.Data!.NextRunAt);
        tasks.Verify(r => r.AddAsync(It.IsAny<ScheduledFarmTask>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
