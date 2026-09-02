namespace Modulus.EntityFrameworkCore.Design;

using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Events;

/// <summary>
/// Stub runtime services for constructing a <see cref="ModuleDbContext"/> at
/// <b>design time</b> — from an <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c>
/// invoked by the EF Core tools (<c>dotnet ef</c> / <c>modulus migrate</c>) where
/// no DI container or HTTP request exists.
/// </summary>
/// <remarks>
/// A <see cref="ModuleDbContext"/> constructor requires <see cref="ICurrentTenant"/>,
/// <see cref="ICurrentUser"/>, a <see cref="DomainEventDispatcher"/> and an
/// <see cref="IServiceProvider"/>. At design time the tools only build the model
/// (<c>OnModelCreating</c>) to scaffold or diff migrations; the tenant/user query
/// filters are <em>captured</em> but never evaluated, and no domain events are
/// dispatched. So no-op stubs are safe and let a design-time factory stay a few
/// lines:
/// <code>
/// public MyDbContext CreateDbContext(string[] args)
/// {
///     var options = new DbContextOptionsBuilder&lt;MyDbContext&gt;()
///         .UseSqlServer(connectionString).Options;
///     return new MyDbContext(
///         options,
///         DesignTimeContext.Tenant,
///         DesignTimeContext.User,
///         DesignTimeContext.Dispatcher,
///         DesignTimeContext.Services);
/// }
/// </code>
/// </remarks>
public static class DesignTimeContext
{
    /// <summary>An empty <see cref="IServiceProvider"/> that resolves nothing.</summary>
    public static IServiceProvider Services { get; } = new EmptyServiceProvider();

    /// <summary>Host (no-tenant) context — query filters degrade to match-all.</summary>
    public static ICurrentTenant Tenant { get; } = new NullCurrentTenant();

    /// <summary>Unauthenticated user — audit fields stamp "system".</summary>
    public static ICurrentUser User { get; } = new NullCurrentUser();

    /// <summary>
    /// Dispatcher backed by the empty provider. Never invoked at design time
    /// (no <c>SaveChanges</c>), so its inability to resolve handlers is moot.
    /// </summary>
    public static DomainEventDispatcher Dispatcher { get; } = new DomainEventDispatcher(Services);

    /// <summary>
    /// Emulates the MS DI contract the model-building path relies on:
    /// <c>IEnumerable&lt;T&gt;</c> resolves to an <em>empty sequence</em> (never
    /// null), so <c>GetServices&lt;IModuleModelContributor&gt;()</c> and similar
    /// seams work without a real container. Everything else resolves to null.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType.IsGenericType
                && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    ? Array.CreateInstance(serviceType.GetGenericArguments()[0], 0)
                    : null;
    }
}
