namespace Modulus.Settings;

/// <summary>
/// Raw per-scope setting value storage. Keys are explicit
/// (<c>name + tenantId + userId + branchId</c>) so the store stays tenant-isolated even
/// when the ambient context is unavailable (background jobs, tests).
/// A branch value is keyed by its company and branch, so no other company's branch can read it.
/// Resolution precedence (user → branch → tenant → global → definition default)
/// lives in <see cref="ISettingManager"/>.
/// </summary>
public interface ISettingStore
{
    Task<string?> GetOrNullAsync(
        string name,
        Guid? tenantId,
        Guid? userId,
        Guid? branchId,
        CancellationToken ct = default);

    Task SetAsync(
        string name,
        string? value,
        Guid? tenantId,
        Guid? userId,
        Guid? branchId,
        CancellationToken ct = default);

    Task RemoveAsync(
        string name,
        Guid? tenantId,
        Guid? userId,
        Guid? branchId,
        CancellationToken ct = default);
}
