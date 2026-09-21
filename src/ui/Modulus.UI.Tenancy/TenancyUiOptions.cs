namespace Modulus.UI.Tenancy;

/// <summary>Permission names declared by the Tenancy UI.</summary>
public static class TenancyUiPermissions
{
    /// <summary>View the tenant directory and details pages.</summary>
    public const string View = "tenancy:view";
}

/// <summary>Options for the Tenancy UI, bound from the <c>TenancyUi</c> section.</summary>
public sealed class TenancyUiOptions
{
    public const string SectionName = "TenancyUi";

    /// <summary>
    /// Razor Pages authorization policy applied to the <c>/Tenancy</c> folder.
    /// Defaults to <see cref="TenancyUiPermissions.View"/> — every page model
    /// also carries a bare <c>[Authorize]</c> as an unconditional floor, so
    /// anonymous access is impossible even before
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention
    /// (without it, requests hit an unresolvable-policy error instead of
    /// silently serving admin pages — call <c>AddModulusAuthorization</c> to
    /// fix it). Set to <c>null</c> or <c>""</c> to opt out of the permission
    /// check and keep only the authentication floor.
    /// </summary>
    public string? RequirePermission { get; set; } = TenancyUiPermissions.View;
}
