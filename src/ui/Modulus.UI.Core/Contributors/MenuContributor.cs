namespace Modulus.UI;

/// <summary>
/// Contributes to (or reshapes) the navigation tree after every UI module has
/// merged its own entries. Runs in registration order inside
/// <see cref="UiNavigationRegistry.GetMenu"/>; use the builder's
/// <c>Add*</c>/<c>Find</c>/<c>MoveTo</c>/<c>SetOrder</c>/<c>Remove</c>
/// mutations. Registered via <c>AddMenuContributor&lt;T&gt;()</c>; keep
/// implementations stateless so singleton lifetime stays safe.
/// </summary>
public interface IMenuContributor
{
    /// <summary>Configures the merged navigation tree.</summary>
    ValueTask ConfigureAsync(MenuConfigurationContext context, CancellationToken cancellationToken = default);
}

/// <summary>Context passed to <see cref="IMenuContributor"/> implementations.</summary>
/// <param name="main">The merged navigation builder (all module entries applied).</param>
public sealed class MenuConfigurationContext(UiNavigationBuilder main)
{
    /// <summary>The merged navigation builder (all module entries applied).</summary>
    public UiNavigationBuilder Main { get; } = main ?? throw new ArgumentNullException(nameof(main));
}
