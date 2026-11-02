namespace Modulus.Authorization;

/// <summary>
/// The two spellings of a permission name: <c>module:resource:action</c> and <c>Module.Resource.Action</c>. Everything that
/// decides whether a string is a permission, a wildcard, or which module it belongs to goes through here.
/// </summary>
public static class PermissionNames
{
    /// <summary>True for a wildcard grant: <c>module:*</c> or <c>Module.Resource.*</c>.</summary>
    public static bool IsWildcard(string? permission)
        => permission is not null
           && (permission.EndsWith(":*", StringComparison.Ordinal) || permission.EndsWith(".*", StringComparison.Ordinal));

    /// <summary>True when a policy name is a permission: it contains <c>:</c>, or has at least three dot-separated segments.</summary>
    public static bool IsPermissionPolicy(string policyName)
        => policyName.Contains(':') || (policyName.Split('.').Length >= 3 && !policyName.Contains(' '));

    /// <summary>The module part: everything before the first <c>:</c> or <c>.</c>.</summary>
    public static string ModuleOf(string permission)
    {
        var end = permission.AsSpan().IndexOfAny(':', '.');
        return end < 0 ? permission : permission[..end];
    }

    /// <summary>True when <paramref name="permission"/> belongs to <paramref name="module"/> (either separator).</summary>
    public static bool InModule(string permission, string module)
        => permission.Length > module.Length
           && permission.StartsWith(module, StringComparison.OrdinalIgnoreCase)
           && permission[module.Length] is ':' or '.';
}
