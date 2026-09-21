using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// Scoped per-request view over the modular menu, filtered through the ambient
/// <see cref="ICurrentUser"/> (fail-closed <c>NullCurrentUser</c> when no
/// Identity module is present, so permission-gated entries simply hide).
/// Inject this in layouts and pages instead of the singleton
/// <see cref="IUiNavigationRegistry"/>, which stays unfiltered by design.
/// </summary>
public interface IUiMenuProvider
{
    /// <summary>Menu visible to the current user, ordered by Order then title.</summary>
    IReadOnlyList<UiMenuItem> GetMenu();
}

/// <inheritdoc />
public sealed class UiMenuProvider(
    IUiNavigationRegistry registry,
    ICurrentUser currentUser) : IUiMenuProvider
{
    /// <inheritdoc />
    public IReadOnlyList<UiMenuItem> GetMenu()
        => UiMenuFilter.Apply(
            registry.GetMenu(),
            p => currentUser.HasPermission(p));
}
