namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Persistence record for a tenant in the EF-backed <see cref="ITenantStore"/>.
/// Kept deliberately small — resolution only needs id, slug, display name, and an
/// active flag. Applications that need richer tenant metadata (plan, region,
/// per-tenant connection string for database-per-tenant) should model that in
/// their own table and join on <see cref="Id"/> rather than widen this one.
/// </summary>
public class TenantEntity
{
    /// <summary>Stable tenant identifier (the value written to <c>TenantId</c> columns).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Human-friendly unique key used by header/subdomain resolvers
    /// (e.g. <c>acme</c>). Unique across the store.
    /// </summary>
    public string Slug { get; set; } = default!;

    /// <summary>Optional display name shown in admin UIs.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// When <see langword="false"/> the tenant is treated as if it does not exist:
    /// resolution returns <see langword="null"/> so a deactivated tenant fails
    /// closed (no request can resolve into it) without deleting its data.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Creation timestamp (UTC), set by <see cref="TenantManager"/>.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
