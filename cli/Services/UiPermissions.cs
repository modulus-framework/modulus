using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>A permission column of the matrix: the registry key and the heading shown.</summary>
internal sealed record MatrixPermission(string Key, string Label);

/// <summary>The inputs of <c>ui create-permission-matrix</c>: which roles are rows and which permissions are columns.</summary>
internal static partial class UiPermissions
{
    public const string ApiPrefix = "/authorization";
    public static readonly string[] DefaultRoles = ["Admin", "Manager", "User"];

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,39}$")]
    private static partial Regex RolePattern();

    [GeneratedRegex("^[a-z][a-z0-9_.-]*(:[a-z0-9_.*-]+)+$")]
    private static partial Regex PermissionPattern();

    /// <summary>Role names that go into generated markup and script: letters, digits, dot, dash and underscore only.</summary>
    public static IReadOnlyList<string> ParseRoles(string? roles)
    {
        var list = (roles is null ? DefaultRoles : roles.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal).ToList();
        if (list.Count == 0) throw new ArgumentException("Name at least one role, e.g. --roles Admin,Manager.");
        foreach (var r in list)
            if (!RolePattern().IsMatch(r))
                throw new ArgumentException($"'{r}' is not a usable role name: start with a letter; use letters, digits, '.', '-' or '_' (40 characters at most).");
        return list;
    }

    /// <summary>Permission keys like <c>orders:order:manage</c>; the heading is the last part, capitalised.</summary>
    public static IReadOnlyList<MatrixPermission> ParsePermissions(string? permissions, string defaultKey)
    {
        var keys = (permissions is null ? [defaultKey] : permissions.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal).ToList();
        foreach (var k in keys)
            if (!PermissionPattern().IsMatch(k))
                throw new ArgumentException($"'{k}' is not a permission key. Use lower-case parts joined by ':', e.g. orders:order:manage.");
        return [.. keys.Select(k => new MatrixPermission(k, Heading(k)))];
    }

    private static string Heading(string key)
    {
        var parts = key.Split(':');
        var last = parts[^1];
        return last == "*" ? $"{parts[^2]} (all)" : $"{char.ToUpperInvariant(last[0])}{last[1..]}";
    }

    /// <summary>The page name the MVC controller derives from a kebab-case route: <c>order-item</c> → <c>OrderItem</c>.</summary>
    public static string PageName(string route) =>
        string.Concat(route.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
}
