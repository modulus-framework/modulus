namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Persistence record for a user's membership in a tenant (company), read by
/// <see cref="EfTenantMembershipStore"/>. One row per (user, tenant); an inactive row is kept for
/// history and grants nothing.
/// </summary>
public class TenantMembershipEntity
{
    /// <summary>The member's user id (the <c>sub</c> / name-identifier claim).</summary>
    public Guid UserId { get; set; }

    /// <summary>The tenant the membership grants entry to.</summary>
    public Guid TenantId { get; set; }

    /// <summary>When <see langword="false"/> the membership is revoked.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
