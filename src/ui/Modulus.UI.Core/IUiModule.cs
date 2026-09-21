namespace Modulus.UI;

/// <summary>
/// Contract for a reusable UI module (the Razor/RCL sidecar of an API module).
/// Implement once per UI area and register via
/// <c>services.AddUiModule&lt;TModule&gt;()</c>.
/// </summary>
/// <remarks>
/// Mirrors <c>IModule</c> deliberately: registration order is authoritative for
/// menu ordering. Keep this interface UI-only (navigation, manifest); backend
/// services stay in the API module's <c>IModule</c>.
/// </remarks>
public interface IUiModule
{
    /// <summary>Describes this module for CLI resolution and <c>/_ui/modules</c>.</summary>
    ModuleManifest Manifest { get; }

    /// <summary>Contributes sidebar entries to the shared navigation.</summary>
    void ConfigureNavigation(UiNavigationBuilder navigation);
}

/// <summary>
/// Convenience base for UI modules. Provides no-op navigation so derived classes
/// override only what they need.
/// </summary>
public abstract class UiModule : IUiModule
{
    /// <inheritdoc />
    public abstract ModuleManifest Manifest { get; }

    /// <inheritdoc />
    public virtual void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
    }
}
