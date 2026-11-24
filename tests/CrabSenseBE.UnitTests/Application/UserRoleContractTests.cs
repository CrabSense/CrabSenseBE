using CrabSenseBE.Domain.Enums;
using Xunit;

namespace CrabSenseBE.UnitTests.Application;

public class UserRoleContractTests
{
    [Theory]
    [InlineData("SystemAdministrator", UserRole.SystemAdministrator)]
    [InlineData("FarmAdministrator", UserRole.FarmAdministrator)]
    [InlineData("FarmManager", UserRole.FarmManager)]
    [InlineData("FarmStaff", UserRole.FarmStaff)]
    public void Canonical_roles_round_trip(string raw, UserRole expected)
    {
        Assert.True(UserRoleParsing.TryParse(raw, out var role));
        Assert.Equal(expected, role);
        Assert.Equal(expected, UserRoleParsing.FromStorage(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("SystemAdmin")]
    [InlineData("FarmOwner")]
    [InlineData("Staff")]
    [InlineData("Admin")]
    [InlineData("farmstaff")]
    [InlineData(" FarmStaff ")]
    [InlineData("unknown")]
    public void Invalid_roles_never_grant_access(string? raw)
    {
        Assert.False(UserRoleParsing.TryParse(raw, out _));
        Assert.Throws<InvalidOperationException>(() => UserRoleParsing.FromStorage(raw));
    }
}
