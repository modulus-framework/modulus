namespace Modulus.Testing.Internal;

using System.Reflection;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Replaces the options of every registered <c>DbContextOptions&lt;TContext&gt;</c>
    /// with <c>UseSqlite(<paramref name="connectionString"/>)</c>. The context and
    /// its <see cref="DbContext"/> alias registrations are left intact — only the
    /// provider options change.
    /// </summary>
    public static void UseSharedSqlite(
        this IServiceCollection services, string connectionString)
    {
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

        Action<DbContextOptionsBuilder> configure =
            options => options.UseSqlite(connectionString);

        foreach (var contextType in contextTypes)
        {
            AddDbContext
                .MakeGenericMethod(contextType)
                .Invoke(null, [services, configure, ServiceLifetime.Scoped, ServiceLifetime.Scoped]);
        }
    }
}
