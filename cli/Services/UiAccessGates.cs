namespace Modulus.Cli.Services;

/// <summary>Shared rules for the Admin role and the per-entity CRUD permission (<c>{module}:{route}:manage</c>) that generated API endpoints use.</summary>
internal static class UiAccessGates
{
    /// <summary>The role the generated identity backend seeds the first administrator into (<c>IdentitySeeding.AdminRole</c>).</summary>
    public const string AdminRole = "Admin";

    /// <summary>Resolves <c>ICurrentUser.HasPermission</c> from the grant store, so menus and pages see the Admin role's grants.</summary>
    public const string GrantCheckerCall = "builder.Services.AddGrantStorePermissionChecker();";

    /// <summary>Namespace of <see cref="GrantCheckerCall"/>'s extension.</summary>
    public const string GrantCheckerNamespace = "Modulus.Identity.Extensions";

    private const string PageAuthorizationMarker = "AllowAnonymousToFolder(\"/Account\")";

    /// <summary>
    /// True when this <c>Program.cs</c> belongs to a host whose identity backend seeds the <c>Admin</c> role (an app generated with
    /// <c>--auth openiddict</c>, either kind) or that keeps its pages behind a sign-in, so granting a permission to that role means something.
    /// </summary>
    public static bool HasAdminRole(string programContent)
    {
        ArgumentNullException.ThrowIfNull(programContent);
        return HasSignIn(programContent)
            || programContent.Contains("SeedIdentityAsync(", StringComparison.Ordinal);
    }

    /// <summary>What a CRUD permission's registry entry says (shown by the Permissions UI).</summary>
    public static string CrudPermissionDescription(string entityPlural)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityPlural);
        return $"Manage {entityPlural}.";
    }

    /// <summary>True when this <c>Program.cs</c> keeps its pages behind a sign-in, so a role can be granted a permission and mean something.</summary>
    public static bool HasSignIn(string programContent)
    {
        ArgumentNullException.ThrowIfNull(programContent);
        return programContent.Contains(PageAuthorizationMarker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The permission that guards a generated CRUD admin page: <c>{module}:{route}:manage</c> (e.g. <c>catalog:products:manage</c>),
    /// so <c>catalog:*</c> covers a whole module and <c>IPermissionRegistry.GetByModule("catalog")</c> lists it.
    /// </summary>
    public static string CrudPermission(string moduleName, string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return $"{moduleName.ToLowerInvariant()}:{route.ToLowerInvariant()}:manage";
    }

    /// <summary>The line that grants <paramref name="permission"/> to the Admin role.</summary>
    public static string GrantCall(string permission)
        => $"builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"{AdminRole}\", \"{permission}\"));";

    /// <summary>What to look for in <c>Program.cs</c> to see the grant is already there (even inside a hand-written call).</summary>
    public static string GrantMarker(string permission)
        => $"GrantToRole(\"{AdminRole}\", \"{permission}\")";
}
