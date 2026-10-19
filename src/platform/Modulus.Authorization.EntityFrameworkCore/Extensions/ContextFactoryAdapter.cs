using Microsoft.EntityFrameworkCore;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Presents an application's derived context factory under a base context type, so stores that depend only on the base (or on a
/// generic instantiation of it) resolve the application's context. <c>IDbContextFactory&lt;T&gt;</c> is invariant, so the upcast
/// has to be an adapter.
/// </summary>
internal sealed class ContextFactoryAdapter<TBase, TContext>(IDbContextFactory<TContext> inner) : IDbContextFactory<TBase>
    where TBase : DbContext
    where TContext : TBase
{
    /// <inheritdoc />
    public TBase CreateDbContext() => inner.CreateDbContext();

    /// <inheritdoc />
    public async Task<TBase> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => await inner.CreateDbContextAsync(cancellationToken);
}
