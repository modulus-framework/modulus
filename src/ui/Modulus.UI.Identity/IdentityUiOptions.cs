namespace Modulus.UI.Identity;

/// <summary>
/// Toggles for the Identity UI. Binds the <c>IdentityUi</c> section.
/// </summary>
public sealed class IdentityUiOptions
{
    public const string SectionName = "IdentityUi";

    /// <summary>
    /// Exposes the <c>/Account/Register</c> page. Default <c>false</c>
    /// (fail-closed, matching the framework's deny-by-default password grant):
    /// with self-registration off the page returns 404 and users are created
    /// by administrators instead.
    /// </summary>
    public bool AllowSelfRegistration { get; set; }
}
