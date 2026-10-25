namespace Modulus.Cli.Services;

/// <summary>
/// Idempotent <c>Program.cs</c> surgery for the permission a generated CRUD API requires: the policy provider that turns the
/// permission into a check, its declaration in the permission registry and the Admin role's grant. The endpoints declare the
/// permission themselves. Pure string transform (no I/O), so it is unit-testable.
/// </summary>
internal static class ApiPermissionWiring
{
    public static string EnsurePermission(string content, string moduleName, string permission, string description)
        => EnsurePermission(content, content, moduleName, permission, description);

    /// <summary>
    /// <paramref name="host"/> is the API host's whole text, where the permission is looked for; <paramref name="content"/> is the
    /// file the lines go into (the feature services, or Program.cs of a host generated before the split).
    /// </summary>
    /// <summary>
    /// <paramref name="withChecker"/> is false for a host with no identity package (<c>--auth none</c>): the grant checker lives in it,
    /// so it is not added there.
    /// </summary>
    public static string EnsurePermission(string content, string host, string moduleName, string permission, string description, bool withChecker = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var nl = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var lines = new List<string>();
        if (!host.Contains("AddModulusAuthorization(", StringComparison.OrdinalIgnoreCase))
            lines.Add("builder.Services.AddModulusAuthorization();");
        var needsChecker = withChecker && !host.Contains(UiAccessGates.GrantCheckerCall, StringComparison.OrdinalIgnoreCase);
        if (needsChecker)
            lines.Add(UiAccessGates.GrantCheckerCall);
        if (!host.Contains($"Add(\"{permission}\"", StringComparison.OrdinalIgnoreCase))
            lines.Add($"builder.Services.AddPermissions(\"{moduleName.ToLowerInvariant()}\", permissions => permissions.Add(\"{permission}\", \"{description}\"));");
        var granted = host.Contains(UiAccessGates.GrantMarker(permission), StringComparison.OrdinalIgnoreCase)
            || host.Contains(UiAccessGates.LegacyGrantMarker(permission), StringComparison.OrdinalIgnoreCase);
        if (!granted)
            lines.Add(UiAccessGates.GrantCall(permission));

        if (lines.Count == 0)
            return content;

        // The feature services method ends in "return builder;"; a host generated before the split has "var app = builder.Build();".
        var anchor = content.Contains("AddApiFeatures(", StringComparison.Ordinal) ? "return builder;" : "var app = builder.Build();";
        foreach (var line in lines)
            content = InsertBefore(content, anchor, line, nl);

        var usings = new List<string> { "using Modulus.Authorization.Extensions;" };
        if (needsChecker)
            usings.Add($"using {UiAccessGates.GrantCheckerNamespace};");
        foreach (var usingLine in usings.Where(u => !content.Contains(u, StringComparison.Ordinal)))
            content = InsertUsing(content, usingLine, nl);
        return content;
    }

    private static string InsertUsing(string content, string usingLine, string nl)
    {
        // Keep usings together: append after the last top-level using.
        var idx = content.LastIndexOf("\nusing ", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var lineEnd = content.IndexOf('\n', idx + 1);
            if (lineEnd >= 0)
                return content.Insert(lineEnd + 1, usingLine + nl);
        }

        return usingLine + nl + content;
    }

    private static string InsertBefore(string content, string anchor, string insertion, string nl)
    {
        var idx = content.IndexOf(anchor, StringComparison.Ordinal);
        if (idx < 0) return content;
        var start = content.LastIndexOf('\n', Math.Max(idx - 1, 0)) + 1;
        var indent = content[start..idx];
        if (indent.Trim(' ', '\t').Length != 0) indent = string.Empty;
        return content.Insert(start, indent + insertion + nl);
    }
}
