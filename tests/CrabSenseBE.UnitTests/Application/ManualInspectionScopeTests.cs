using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class ManualInspectionScopeTests
{
    [Fact]
    public async Task Staff_cannot_submit_inspection_outside_assigned_area()
    {
        var staffId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        var boxB = Guid.NewGuid();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(staffId);

        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = staffId,
                Username = "staff-a",
                Role = UserRole.FarmStaff,
                IsActive = true
            });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = staffId,
                ManagerUserId = Guid.NewGuid(),
                FarmingAreaId = areaA
            });
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(repo => repo.GetByIdAsync(boxB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxB, FarmingRowId = rowB, Code = "BOX-B" });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(repo => repo.GetByIdAsync(rowB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = rowB, FarmingAreaId = areaB, Code = "ROW-B" });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(assignments.Object);
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);

        var service = new ManualInspectionService(uow.Object, current.Object);
        var act = () => service.SubmitAsync(new SubmitManualInspectionRequest(boxB));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
        uow.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
