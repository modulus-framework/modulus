namespace Modulus.Testing.Internal;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Records what the SQLite swap discovered, so the keep-alive hosted service
/// can cover every swapped database — including contexts registered only via
/// <c>IDbContextFactory&lt;T&gt;</c>, which never appear in
/// <c>GetServices&lt;DbContext&gt;()</c>.
/// </summary>
internal sealed class TestDatabaseRegistry
{
    public HashSet<Type> FactoryContextTypes { get; } = [];
}

/// <summary>
/// Rewires every registered module <see cref="DbContext"/> to a shared in-memory
/// SQLite database so integration tests never touch the module's real provider
/// (SQL Server, PostgreSQL, …). Called from
/// <see cref="ModulusWebAppFactory{TEntryPoint}"/> inside
/// <c>ConfigureTestServices</c>, i.e. after the application has registered its
/// modules but before the container is built.
/// </summary>
internal static class TestDatabaseRegistration
{
    // The AddDbContext<TContext>(services, Action<DbContextOptionsBuilder>,
    // ServiceLifetime, ServiceLifetime) overload — resolved once and reused per
    // module context. Selected explicitly so a future EF overload addition can't
    // make the lookup ambiguous.
    private static readonly MethodInfo AddDbContext = typeof(EntityFrameworkServiceCollectionExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(EntityFrameworkServiceCollectionExtensions.AddDbContext)
            && m.IsGenericMethodDefinition
            && m.GetGenericArguments().Length == 1
            && m.GetParameters() is { Length: 4 } p
            && p[1].ParameterType == typeof(Action<DbContextOptionsBuilder>)
            && p[2].ParameterType == typeof(ServiceLifetime)
            && p[3].ParameterType == typeof(ServiceLifetime));

    // The AddDbContextFactory<TContext>(services, configure, factoryLifetime)
    // overload — used for contexts the app registered through a factory (e.g.
    // AddEfCoreAuthorizationStores) so their IDbContextFactory<T> survives the
    // swap instead of being silently dropped and leaving factory-dependent
    // singletons (EfPermissionGrantStore, …) unresolvable.
    private static readonly MethodInfo AddDbContextFactory = typeof(EntityFrameworkServiceCollectionExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(EntityFrameworkServiceCollectionExtensions.AddDbContextFactory)
            && m.IsGenericMethodDefinition
            && m.GetGenericArguments().Length == 1
            && m.GetParameters() is { Length: 3 } p
            && p[1].ParameterType == typeof(Action<DbContextOptionsBuilder>)
            && p[2].ParameterType == typeof(ServiceLifetime));

    /// <summary>
    /// Replaces the options of every registered <c>DbContextOptions&lt;TContext&gt;</c>
    /// with <c>UseSqlite(<paramref name="connectionString"/>)</c>. The context and
    /// its <see cref="DbContext"/> alias registrations are left intact — only the
    /// provider options change.
    /// </summary>
    public static void UseSharedSqlite(
        this IServiceCollection services, string connectionString)
        => SwapToSqlite(services, _ => connectionString);

    /// <summary>
    /// Like <see cref="UseSharedSqlite"/>, but gives <b>each</b> registered module
    /// context its <b>own</b> in-memory SQLite database named after the context
    /// (e.g. <c>Data Source={prefix}-CatalogDbContext;Mode=Memory;Cache=Shared</c>).
    /// Returns the context-type → connection-string map so the caller can open a
    /// keep-alive connection per database.
    /// </summary>
    /// <remarks>
    /// Required for multi-module apps: <c>EnsureCreated</c> short-circuits when the
    /// database already has tables, so contexts sharing one database would have
    /// their schema silently skipped (the second module's tables would never be
    /// created). Per-context databases give every module an isolated schema, which
    /// also matches the framework's per-module-database design.
    /// </remarks>
    public static IReadOnlyDictionary<Type, string> UsePerContextSqlite(
        this IServiceCollection services, string databasePrefix,
        TestDatabaseRegistry? registry = null)
    {
        var map = new Dictionary<Type, string>();
        SwapToSqlite(services, contextType =>
        {
            var connectionString =
                $"Data Source={databasePrefix}-{contextType.Name};Mode=Memory;Cache=Shared";
            map[contextType] = connectionString;
            return connectionString;
        }, registry);
        return map;
    }

    private static void SwapToSqlite(
        IServiceCollection services, Func<Type, string> connectionFor,
        TestDatabaseRegistry? registry = null)
    {
        // Contexts the app registered ONLY through IDbContextFactory<TContext>
        // (never as a scoped DbContext) must be re-registered through a factory
        // as well: the descriptor sweep below matches any service type closed
        // over the context type, which includes the factory itself.
        var factoryContexts = services
            .Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextFactory<>))
            .Select(d => d.ServiceType.GetGenericArguments()[0])
            .ToHashSet();
        if (registry is not null)
            foreach (var contextType in factoryContexts)
                registry.FactoryContextTypes.Add(contextType);

        // One DbContextOptions<TContext> is registered per module context.
        var contextTypes = services
            .Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
            .Select(d => d.ServiceType.GetGenericArguments()[0])
            .Distinct()
            .ToHashSet();

        // Drop every options-related registration for those contexts, then let
        // AddDbContext re-create them against SQLite. It is not enough to remove
        // DbContextOptions<TContext>: since EF Core 9 the provider is applied via a
        // separate IDbContextOptionsConfiguration<TContext> descriptor, so leaving
        // it behind would apply BOTH the module's provider and SQLite to one
        // context ("multiple providers registered"). Both are closed generics over
        // the context type, so match on the generic argument; also drop the shared
        // non-generic DbContextOptions.
        var stale = services
            .Where(d => d.ServiceType == typeof(DbContextOptions)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericArguments().Any(contextTypes.Contains)))
            .ToList();
        foreach (var descriptor in stale)
            services.Remove(descriptor);

        foreach (var contextType in contextTypes)
        {
            var connectionString = connectionFor(contextType);
            Action<DbContextOptionsBuilder> configure =
                options => options.UseSqlite(connectionString);
            if (factoryContexts.Contains(contextType))
                AddDbContextFactory
                    .MakeGenericMethod(contextType)
                    .Invoke(null, [services, configure, ServiceLifetime.Singleton]);
            else
                AddDbContext
                    .MakeGenericMethod(contextType)
                    .Invoke(null, [services, configure, ServiceLifetime.Scoped, ServiceLifetime.Scoped]);
        }
    }
}
