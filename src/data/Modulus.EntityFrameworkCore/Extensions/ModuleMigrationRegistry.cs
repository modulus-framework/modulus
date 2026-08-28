using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.EntityFrameworkCore.Extensions;

/// <summary>
/// Tracks which module <see cref="DbContext"/> types are managed externally
/// (e.g. by dbsh or another out-of-band migration tool) and should be
/// skipped by <see cref="DatabaseMigrationExtensions.MigrateModulusDatabasesAsync"/>.
/// </summary>
public interface IModuleMigrationRegistry
{
    bool IsExternallyManaged(Type contextType);
}

/// <summary>
/// Thread-safe registry populated at startup via
/// <see cref="ModuleMigrationServiceExtensions.ExternallyManaged"/>.
/// </summary>
internal sealed class ModuleMigrationRegistry : IModuleMigrationRegistry
{
    private readonly ConcurrentDictionary<Type, bool> _entries = new();

    public void Register(Type contextType)
        => _entries[contextType] = true;

    public bool IsExternallyManaged(Type contextType)
        => _entries.TryGetValue(contextType, out var managed) && managed;
}

/// <summary>
/// Extension methods for registering module migration metadata.
/// </summary>
public static class ModuleMigrationServiceExtensions
{
    /// <summary>
    /// Marks the module <typeparamref name="TContext"/> as <b>externally managed</b>
    /// — <see cref="DatabaseMigrationExtensions.MigrateModulusDatabasesAsync"/>
    /// will skip it (no <c>Migrate</c> / <c>EnsureCreated</c>). Use when the
    /// schema is managed by an external tool such as <c>dbsh</c>.
    /// </summary>
    public static IServiceCollection ExternallyManaged<TContext>(
        this IServiceCollection services)
        where TContext : DbContext
    {
        GetOrAddRegistry(services).Register(typeof(TContext));
        return services;
    }

    /// <summary>
    /// Returns the single <see cref="ModuleMigrationRegistry"/> shared across all
    /// <see cref="ExternallyManaged{TContext}"/> calls, creating and registering it
    /// (and the <see cref="IModuleMigrationRegistry"/> that reads it) on first use.
    /// </summary>
    internal static ModuleMigrationRegistry GetOrAddRegistry(
        IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(ModuleMigrationRegistry))
                ?.ImplementationInstance is ModuleMigrationRegistry existing)
            return existing;

        var registry = new ModuleMigrationRegistry();
        services.AddSingleton(registry);
        services.TryAddSingleton<IModuleMigrationRegistry>(registry);
        return registry;
    }
}
