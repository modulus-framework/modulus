using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy;

/// <summary>
/// Default <see cref="ITenantStore"/> used when no tenant store has been
/// configured. Returns no tenants (<c>null</c>), so tenant resolution yields
/// an empty/host context rather than throwing an unresolved-service exception.
/// Replace by registering a real <see cref="ITenantStore"/> (e.g. backed by a
/// module DbContext) before calling the resolvers.
/// </summary>
public sealed class NullTenantStore : ITenantStore
{
    public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult<TenantInfo?>(null);

    public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
        => Task.FromResult<TenantInfo?>(null);
}
