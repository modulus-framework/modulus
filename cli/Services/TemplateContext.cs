namespace Modulus.Cli.Services;

/// <summary>
/// Context object passed to template rendering.
/// Contains entity metadata and generation settings.
/// </summary>
internal class UiTemplateContext
{
    /// <summary>Entity name (singular, e.g. "Product")</summary>
    public required string Entity { get; init; }

    /// <summary>Entity name plural (e.g. "Products")</summary>
    public required string EntityPlural { get; init; }

    /// <summary>Module model containing namespace, name, etc.</summary>
    public required ModuleModel Module { get; init; }

    /// <summary>UI Engine selected (mvc, razor-pages, blazor, fluid)</summary>
    public required string UiEngine { get; init; }

    /// <summary>Root namespace for generated files</summary>
    public required string Namespace { get; init; }

    /// <summary>Timestamp for generation</summary>
    public DateTime Now { get; } = DateTime.UtcNow;

    /// <summary>Entity name in camelCase (e.g. "product")</summary>
    public required string EntityLower { get; init; }

    /// <summary>Entity name in route format (e.g. "products")</summary>
    public required string RouteName { get; init; }
}
