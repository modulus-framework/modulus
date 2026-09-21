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
    /// Defaults to <c>null</c> (pages open) so the UI works without the
    /// authorization stack; set to <see cref="TenancyUiPermissions.View"/>
    /// once <c>AddModulusAuthorization</c> wires the <c>:</c>-policy
    /// convention. Declared on the registry by <c>AddModulusTenancyUi</c>.
    /// </summary>
    public string? RequirePermission { get; set; }
}
