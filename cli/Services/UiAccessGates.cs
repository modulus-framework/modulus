using System.Text.Json;

namespace Modulus.Cli.Services;

/// <summary>
/// Locks the admin feature UIs (Users, Tenancy, Settings, ...) of a generated web app to administrators. A feature UI gates its
/// pages only when its <c>RequirePermission</c> option is set, and even then only if the permission stack resolves it, so
/// <c>ui add</c> / <c>app --ui-modules</c> wire all three pieces together:
/// <list type="number">
/// <item><c>appsettings.json</c>: <c>"UsersUi": { "RequirePermission": "users:manage" }</c> (the UI reads it when it registers its folder convention);</item>
/// <item><c>Program.cs</c>: <c>AddModulusAuthorization()</c>, the policy provider that turns <c>users:manage</c> into a permission check;</item>
/// <item><c>Program.cs</c>: a grant of that permission to the <c>Admin</c> role, the role the identity backend seeds the first administrator into.</item>
/// </list>
/// It applies only to a host that keeps its pages behind a sign-in (<c>AddModulusPageAuthorization</c>, i.e. the identity backend
/// of a generated web app): a host with no sign-in has no role to grant to, so requiring a permission would lock everyone out.
/// An existing section is never rewritten, so an app that chose its own permission (or none) keeps it.
/// </summary>
internal static class UiAccessGates
{
    /// <summary>The role the generated identity backend seeds the first administrator into (<c>IdentitySeeding.AdminRole</c>).</summary>
    public const string AdminRole = "Admin";

    /// <summary>Resolves <c>ICurrentUser.HasPermission</c> from the grant store, so menus and pages see the Admin role's grants.</summary>
    public const string GrantCheckerCall = "builder.Services.AddGrantStorePermissionChecker();";

    /// <summary>Namespace of <see cref="GrantCheckerCall"/>'s extension.</summary>
    public const string GrantCheckerNamespace = "Modulus.Identity.Extensions";

    private const string PageAuthorizationMarker = "AddModulusPageAuthorization(";

    /// <summary>True when <paramref name="module"/> is gated and this <c>Program.cs</c> has the sign-in the gate relies on.</summary>
    public static bool Applies(string programContent, UiModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return module.Gate is not null && HasSignIn(programContent);
    }

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

    /// <summary>
    /// Returns a generated admin page's <c>Index.cshtml.cs</c> with <c>[Authorize(Policy = permission)]</c> on its <c>IndexModel</c>
    /// (and the using it needs), for a page generated before CRUD pages carried a permission. A page that already has any
    /// <c>[Authorize</c> (the app's own choice) or no <c>IndexModel</c> is returned unchanged.
    /// </summary>
    public static string EnsurePageGuard(string pageModelSource, string permission)
    {
        ArgumentNullException.ThrowIfNull(pageModelSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        if (pageModelSource.Contains("[Authorize", StringComparison.Ordinal))
        {
            return pageModelSource;
        }

        var declaration = System.Text.RegularExpressions.Regex.Match(
            pageModelSource,
            @"^public (?:sealed )?class IndexModel\b",
            System.Text.RegularExpressions.RegexOptions.Multiline,
            TimeSpan.FromSeconds(2));
        if (!declaration.Success)
        {
            return pageModelSource;
        }

        var newline = pageModelSource.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var guarded = pageModelSource.Insert(declaration.Index, $"[Authorize(Policy = \"{permission}\")]{newline}");

        const string Using = "using Microsoft.AspNetCore.Authorization;";
        if (guarded.Contains(Using, StringComparison.Ordinal))
        {
            return guarded;
        }

        // Keep the usings sorted the way the template writes them: System/Microsoft first.
        var mvc = guarded.IndexOf("using Microsoft.AspNetCore.Mvc;", StringComparison.Ordinal);
        return guarded.Insert(mvc >= 0 ? mvc : 0, Using + newline);
    }

    /// <summary>The line that grants <paramref name="permission"/> to the Admin role.</summary>
    public static string GrantCall(string permission)
        => $"builder.Services.AddPermissionGrants(grants => grants.GrantToRole(\"{AdminRole}\", \"{permission}\"));";

    /// <summary>What to look for in <c>Program.cs</c> to see the grant is already there (even inside a hand-written call).</summary>
    public static string GrantMarker(string permission)
        => $"GrantToRole(\"{AdminRole}\", \"{permission}\")";

    /// <summary>The line that grants the gate's permission to the Admin role.</summary>
    public static string GrantCall(UiAccessGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        return GrantCall(gate.Permission);
    }

    /// <summary>What to look for in <c>Program.cs</c> to see the grant is already there (even inside a hand-written call).</summary>
    public static string GrantMarker(UiAccessGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        return GrantMarker(gate.Permission);
    }

    /// <summary>
    /// Returns <paramref name="json"/> with the gate's options section added, or unchanged when the section already exists
    /// (whatever it says) or the file is not valid JSON. The section is appended textually, so the rest of the file keeps its
    /// formatting and comments.
    /// </summary>
    public static string EnsureSettings(string json, UiAccessGate gate)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(gate);

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        try
        {
            using var document = JsonDocument.Parse(json, options);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().Any(p => string.Equals(p.Name, gate.Section, StringComparison.OrdinalIgnoreCase)))
            {
                return json;
            }
        }
        catch (JsonException)
        {
            return json;
        }

        var close = json.LastIndexOf('}');
        if (close < 0)
        {
            return json;
        }

        var newline = json.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var head = json[..close].TrimEnd();
        var comma = head.EndsWith('{') ? string.Empty : ",";
        var section =
            $"  \"{gate.Section}\": {{{newline}" +
            $"    \"RequirePermission\": \"{gate.Permission}\"{newline}" +
            "  }";
        var result = $"{head}{comma}{newline}{section}{newline}{json[close..]}";

        try
        {
            using var _ = JsonDocument.Parse(result, options);
            return result;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// Applies <see cref="EnsureSettings"/> to the <c>appsettings.json</c> beside <paramref name="programCsPath"/> when
    /// <paramref name="module"/> is gated for that host. Returns true when the file was (or, in a dry run, would be) changed.
    /// </summary>
    public static bool WriteSettings(string programCsPath, UiModuleDefinition module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programCsPath);
        ArgumentNullException.ThrowIfNull(module);

        if (module.Gate is null || !File.Exists(programCsPath) || !Applies(File.ReadAllText(programCsPath), module))
        {
            return false;
        }

        var settings = Path.Combine(Path.GetDirectoryName(programCsPath) ?? ".", "appsettings.json");
        if (!File.Exists(settings))
        {
            return false;
        }

        var original = File.ReadAllText(settings);
        var updated = EnsureSettings(original, module.Gate);
        if (updated == original)
        {
            return false;
        }

        Ux.WriteFile(settings, updated);
        return true;
    }
}
