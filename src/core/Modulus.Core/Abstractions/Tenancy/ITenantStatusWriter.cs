namespace Modulus.Core.Abstractions;

/// <summary>
/// Changes a company's lifecycle status (trial, active, suspended, closed). Implemented by the tenant store's manager, so
/// code that needs to suspend a company (offboarding) does not depend on the store's database package. The change takes
/// effect on the company's next request.
/// </summary>
public interface ITenantStatusWriter
{
    /// <summary>Sets the status; returns false when no company has the id.</summary>
    Task<bool> SetStatusAsync(Guid id, TenantStatus status, DateTimeOffset? trialEndsAt = null, CancellationToken ct = default);
}
