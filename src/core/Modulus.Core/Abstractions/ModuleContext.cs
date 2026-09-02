namespace Modulus.Core.Abstractions;

/// <summary>
/// Contextual information passed to IModule.InitializeAsync.
/// </summary>
public sealed class ModuleContext
{
    /// <summary>Root DI service provider. Use only during initialisation.</summary>
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>Application configuration root.</summary>
    public required IConfiguration Configuration { get; init; }

    /// <summary>Logger scoped to the module type.</summary>
    public required ILogger Logger { get; init; }

    /// <summary>Descriptor for this module.</summary>
    public required ModuleDescriptor Descriptor { get; init; }
}

/// <summary>Metadata about a loaded module.</summary>
public sealed class ModuleDescriptor
{
    public required string Name { get; init; }
    public required Type ModuleType { get; init; }

    /// <summary>Zero-based position in registration order; also the init order.</summary>
    public required int InitOrder { get; init; }
}
