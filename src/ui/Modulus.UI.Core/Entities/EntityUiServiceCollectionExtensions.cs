using Microsoft.Extensions.DependencyInjection;

namespace Modulus.UI;

/// <summary>DI entry point for contributing to another module's entity forms.</summary>
public static class EntityUiServiceCollectionExtensions
{
    /// <summary>
    /// Contributes to the UI of <paramref name="entity"/> (<c>Catalog.Product</c>) from another module, e.g.
    /// <c>e.Fields.Add(new EntityField("ReorderLevel", typeof(decimal), "Reorder level", tab: "Inventory"))</c>.
    /// Call it from a module's <c>ConfigureServices</c>: contributions run in registration order, so a later
    /// module may <c>Fields.Remove</c> what an earlier one added. Calls for the same entity accumulate.
    /// </summary>
    public static IServiceCollection ConfigureEntityUi(
        this IServiceCollection services,
        string entity,
        Action<EntityUiBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddModulusUi();
        services.Configure<EntityUiOptions>(o => o.Add(entity, configure));
        return services;
    }
}
