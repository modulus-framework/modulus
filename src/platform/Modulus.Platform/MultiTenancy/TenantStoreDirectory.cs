using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy;

/// <summary>Adapts the registered <see cref="ITenantStore"/> to <see cref="ITenantDirectory"/> for background passes.</summary>
internal sealed class TenantStoreDirectory(ITenantStore store) : ITenantDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(CancellationToken ct = default)
        => [.. (await store.ListAsync(ct)).Select(t => t.TenantId)];
}
