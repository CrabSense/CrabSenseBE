using CrabSenseBE.Application;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class FarmAreaScopeTests
{
    [Fact]
    public async Task Injected_unauthenticated_context_is_rejected()
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(false);
        var scope = new FarmAreaScope(Mock.Of<IUnitOfWork>(), current.Object);

        var act = () => scope.AreaIdsAsync(CancellationToken.None);

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Missing_context_is_an_explicit_unit_test_bypass()
    {
        var scope = new FarmAreaScope(Mock.Of<IUnitOfWork>());

        var result = await scope.AreaIdsAsync(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Authenticated_staff_without_assignment_gets_empty_scope()
    {
        var userId = Guid.NewGuid();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(userId);

        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = userId,
                Username = "unassigned-staff",
                Role = UserRole.FarmStaff,
                IsActive = true
            });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FarmStaffAssignment?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(assignments.Object);

        var result = await new FarmAreaScope(uow.Object, current.Object)
            .AreaIdsAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void AddApplication_rejects_missing_current_user_registration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var act = () => services.AddApplication(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ICurrentUserService*");
    }

    [Fact]
    public void Prioritized_scoped_services_require_current_user_dependency()
    {
        Type[] serviceTypes =
        [
            typeof(FarmLotService),
            typeof(HarvestService),
            typeof(FrozenStorageService),
            typeof(TraceabilityService),
            typeof(FarmHistoryService),
            typeof(MortalityService)
        ];

        foreach (var serviceType in serviceTypes)
        {
            var parameter = serviceType.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Single(item => item.ParameterType == typeof(ICurrentUserService));

            parameter.HasDefaultValue.Should().BeFalse(
                $"{serviceType.Name} must fail DI resolution when current-user wiring is missing");
        }
    }

    [Fact]
    public async Task Unassigned_staff_cannot_list_lots_from_other_areas()
    {
        var (uow, current) = UnassignedStaff();
        var lotId = Guid.NewGuid();
        var lots = RepositoryWithAll(new CrabLot { Id = lotId, LotCode = "LOT-OTHER", Name = "Other" });
        var links = RepositoryWithAll(new CrabLotArea
        {
            CrabLotId = lotId,
            FarmingAreaId = Guid.NewGuid()
        });
        uow.Setup(unit => unit.CrabLots).Returns(lots.Object);
        uow.Setup(unit => unit.CrabLotAreas).Returns(links.Object);
        uow.Setup(unit => unit.Crabs).Returns(RepositoryWithAll<Crab>().Object);
        var service = new FarmLotService(uow.Object, Mock.Of<IPublicImageStorage>(), current.Object);

        var response = await service.GetLotsAsync();

        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Unassigned_staff_cannot_infer_global_next_lot_code()
    {
        var (uow, current) = UnassignedStaff();
        var service = new FarmLotService(
            uow.Object, Mock.Of<IPublicImageStorage>(), current.Object);

        var act = () => service.GetNextLotCodeAsync();

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Unassigned_staff_cannot_list_harvest_vouchers()
    {
        var (uow, current) = UnassignedStaff();
        uow.Setup(unit => unit.HarvestVouchers)
            .Returns(RepositoryWithAll(new HarvestVoucher
            {
                Id = Guid.NewGuid(),
                VoucherCode = "HV-OTHER",
                FarmingAreaId = Guid.NewGuid()
            }).Object);
        var service = new HarvestService(uow.Object, current.Object);

        var response = await service.GetAllAsync();

        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Unassigned_staff_cannot_list_frozen_lots()
    {
        var (uow, current) = UnassignedStaff();
        uow.Setup(unit => unit.FrozenLots)
            .Returns(RepositoryWithAll(new FrozenLot
            {
                Id = Guid.NewGuid(),
                LotCode = "FL-OTHER",
                FarmingAreaId = Guid.NewGuid()
            }).Object);
        var service = new FrozenStorageService(uow.Object, current.Object);

        var response = await service.GetAllAsync();

        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Unassigned_staff_cannot_list_box_history_status()
    {
        var (uow, current) = UnassignedStaff();
        var rowId = Guid.NewGuid();
        uow.Setup(unit => unit.FarmingRows)
            .Returns(RepositoryWithAll(new FarmingRow
            {
                Id = rowId,
                FarmingAreaId = Guid.NewGuid(),
                Code = "ROW-OTHER"
            }).Object);
        uow.Setup(unit => unit.Boxes)
            .Returns(RepositoryWithAll(new Box
            {
                Id = Guid.NewGuid(),
                FarmingRowId = rowId,
                Code = "BOX-OTHER"
            }).Object);
        var service = new FarmHistoryService(
            uow.Object, Mock.Of<IPublicImageStorage>(), current.Object);

        var response = await service.ListBoxesFarmingStatusAsync();

        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Unassigned_staff_cannot_list_mortality_records()
    {
        var (uow, current) = UnassignedStaff();
        uow.Setup(unit => unit.GetMortalityRecordsWithDetailsAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CrabMortalityRecord
                {
                    Id = Guid.NewGuid(),
                    CrabId = Guid.NewGuid(),
                    BoxId = Guid.NewGuid()
                }
            ]);
        var service = new MortalityService(uow.Object, current.Object);

        var result = await service.GetMortalityRecordsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Unassigned_staff_cannot_read_internal_product_qr()
    {
        var (uow, current) = UnassignedStaff();
        var lotId = Guid.NewGuid();
        var qrs = new Mock<IRepository<QrCode>>();
        qrs.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QrCode
            {
                Id = Guid.NewGuid(),
                Code = "FL-OTHER",
                EntityType = "frozen_lot",
                FrozenLotId = lotId,
                IsActive = true
            });
        var lots = new Mock<IRepository<FrozenLot>>();
        lots.Setup(repo => repo.GetByIdAsync(lotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FrozenLot
            {
                Id = lotId,
                LotCode = "FL-OTHER",
                FarmingAreaId = Guid.NewGuid()
            });
        uow.Setup(unit => unit.QrCodes).Returns(qrs.Object);
        uow.Setup(unit => unit.FrozenLots).Returns(lots.Object);
        var service = new TraceabilityService(uow.Object, current.Object);

        var act = () => service.GetFrozenLotQrAsync(lotId);

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
    }

    private static (Mock<IUnitOfWork> Uow, Mock<ICurrentUserService> Current) UnassignedStaff()
    {
        var userId = Guid.NewGuid();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(userId);

        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = userId,
                Username = "unassigned-staff",
                Role = UserRole.FarmStaff,
                IsActive = true
            });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FarmStaffAssignment?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(assignments.Object);
        return (uow, current);
    }

    private static Mock<IRepository<T>> RepositoryWithAll<T>(params T[] items)
        where T : BaseEntity
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        return repository;
    }
}
