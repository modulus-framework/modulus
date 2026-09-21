namespace Modulus.UI;

/// <summary>Read-only view over the modular navigation registry.</summary>
public interface IUiNavigationRegistry
{
    /// <summary>Top-level menu ordered by <c>Order</c> then title.</summary>
    IReadOnlyList<UiMenuItem> GetMenu();

    /// <summary>Manifests of all registered UI modules.</summary>
    IReadOnlyList<ModuleManifest> GetModules();
}

/// <summary>
/// Singleton registry backing <see cref="IUiNavigationRegistry"/>. UI modules are
/// injected as <c>IEnumerable&lt;IUiModule&gt;</c> (registered via
/// <c>AddUiModule&lt;T&gt;()</c>); the menu is merged on each call so installing
/// a module changes the sidebar without a freeze step.
/// <para>
/// After all module contributions merge, <see cref="IMenuContributor"/>
/// registrations (apps / cross-cutting modules, via <c>AddMenuContributor&lt;T&gt;()</c>)
/// run in registration order and may add, move, reorder, or remove entries —
/// they see the fully merged tree.
/// </para>
/// </summary>
public sealed class UiNavigationRegistry(
    IEnumerable<IUiModule> modules,
    IEnumerable<IMenuContributor>? contributors = null) : IUiNavigationRegistry
{
    private readonly IReadOnlyList<IUiModule> _modules = modules.ToList();
    private readonly IReadOnlyList<IMenuContributor> _contributors = contributors?.ToList() ?? [];

    /// <inheritdoc />
    public IReadOnlyList<UiMenuItem> GetMenu()
    {
        var builder = new UiNavigationBuilder();
        foreach (var module in _modules)
        {
            var contribution = new UiNavigationBuilder();
            module.ConfigureNavigation(contribution);
            builder.Merge(contribution.Snapshot());
        }

        if (_contributors.Count > 0)
        {
            var context = new MenuConfigurationContext(builder);
            foreach (var contributor in _contributors)
            {
                // GetMenu() is a synchronous contract (layouts, /_ui/menu, tests). Contributors
                // are stateless and typically complete synchronously, and Modulus hosts have no
                // SynchronizationContext, so blocking here cannot deadlock.
#pragma warning disable VSTHRD002
                contributor.ConfigureAsync(context, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
#pragma warning restore VSTHRD002
            }
        }

        return builder.Build();
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleManifest> GetModules()
        => _modules
            .Select(m => m.Manifest)
            .DistinctBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
