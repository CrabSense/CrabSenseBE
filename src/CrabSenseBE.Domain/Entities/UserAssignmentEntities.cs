using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>
/// Một dòng phân công một Farm Manager vào một khu.
/// Manager có thể có nhiều dòng cho nhiều khu khác nhau.
/// </summary>
public class FarmManagerAssignment : BaseEntity
{
    public Guid ManagerUserId { get; set; }
    public AppUser? Manager { get; set; }
    public Guid FarmingAreaId { get; set; }
    public FarmingArea? FarmingArea { get; set; }
    /// <summary>Người gán khu, thường là Farm Administrator.</summary>
    public Guid AssignedByUserId { get; set; }
    public AppUser? AssignedBy { get; set; }
}

/// <summary>Một Farm Staff thuộc một manager và cùng khu với manager đó.</summary>
public class FarmStaffAssignment : BaseEntity
{
    public Guid StaffUserId { get; set; }
    public AppUser? Staff { get; set; }
    public Guid ManagerUserId { get; set; }
    public AppUser? Manager { get; set; }
    public Guid FarmingAreaId { get; set; }
    public FarmingArea? FarmingArea { get; set; }
    /// <summary>Người gán, thường là Farm Administrator.</summary>
    public Guid AssignedByUserId { get; set; }
    public AppUser? AssignedBy { get; set; }
}
