namespace Modulus.UI.Identity;

using Modulus.UI;

/// <summary>
/// UI sidecar for <c>Modulus.Identity</c>: login / registration / sign-out
/// Razor Pages. Auth pages are deliberately <b>excluded</b> from the sidebar
/// menu (they are reached via the cookie middleware's login redirect and the
/// layout's account links, never as navigation entries), so navigation
/// configuration is a no-op — the manifest alone advertises the module on
/// <c>/_ui/modules</c>.
/// </summary>
public sealed class IdentityUiModule : UiModule
{
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.Identity",
        "Identity",
        "1.0.0",
        ["Modulus.Identity", "Modulus.UI.Core"],
        ["Login", "Register", "SignOut"]);
}
