namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// Which host owns its rows of the shared permission catalog. Several hosts can share one authorization database, and each one's startup
/// reconciles only the rows it owns: a host never reports or removes a permission that another host declares.
/// </summary>
public sealed class PermissionCatalogOptions
{
    /// <summary>
    /// The owner of this host's catalog rows, at most 128 characters. Defaults to the entry assembly's name (for example
    /// <c>MyApp.Api</c>), which is stable across restarts. Changing it orphans the rows recorded under the old name.
    /// </summary>
    public string? Owner { get; set; }
}
