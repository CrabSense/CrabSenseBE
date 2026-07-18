namespace CrabSenseBE.Domain.Enums;

public enum UserRole
{
    Admin,
    Operator,
    Viewer,
    SystemAdmin,
    Sales
}

public enum OrderStatus
{
    Quotation,
    Confirmed,
    Packing,
    Shipping,
    Completed,
    Cancelled
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Overdue,
    Cancelled
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertStatus
{
    Active,
    Acknowledged,
    Resolved
}

public enum DeviceStatus
{
    Online,
    Offline,
    Maintenance,
    Error
}

public enum FrozenLotStatus
{
    Available,
    Reserved,
    Shipped,
    Expired
}

public enum HarvestStatus
{
    Planned,
    InProgress,
    Completed,
    Cancelled
}
