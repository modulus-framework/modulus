namespace ProcureFlow.Modules.Vendors.Domain.Entities;

/// <summary>
/// BR-VEN-01 vendor lifecycle. Only the transitions in
/// <see cref="VendorStatusExtensions.CanTransitionTo"/> are legal; every
/// other move is rejected by the aggregate.
/// </summary>
public enum VendorStatus
{
    Draft = 1,
    Submitted = 2,
    UnderReview = 3,
    Qualified = 4,
    Active = 5,
    Suspended = 6,
    Blacklisted = 7,
    Rejected = 8,
}

public static class VendorStatusExtensions
{
    private static readonly Dictionary<VendorStatus, VendorStatus[]> Transitions = new()
    {
        [VendorStatus.Draft] = [VendorStatus.Submitted],
        [VendorStatus.Submitted] = [VendorStatus.UnderReview],
        [VendorStatus.UnderReview] = [VendorStatus.Qualified, VendorStatus.Rejected],
        [VendorStatus.Qualified] = [VendorStatus.Active, VendorStatus.Rejected],
        [VendorStatus.Active] = [VendorStatus.Suspended, VendorStatus.Blacklisted],
        [VendorStatus.Suspended] = [VendorStatus.Active, VendorStatus.Blacklisted],
        [VendorStatus.Blacklisted] = [VendorStatus.Suspended],
        [VendorStatus.Rejected] = [VendorStatus.Draft],
    };

    public static bool CanTransitionTo(this VendorStatus from, VendorStatus to)
        => Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>BR-VEN-08: only Active vendors may transact.</summary>
    public static bool CanTransact(this VendorStatus status) => status == VendorStatus.Active;
}

public enum VendorType
{
    Manufacturer = 1,
    Trader = 2,
    ServiceProvider = 3,
    OverseasAgent = 4,
}
