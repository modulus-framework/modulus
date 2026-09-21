namespace Modulus.Cli.Services;

/// <summary>
/// Idempotent <c>Program.cs</c> surgery for <c>generate-crud --with-ui</c>:
/// registers the generated <c>CustomUiModule</c> nav sidecar (which lives in
/// the Api host, not in a prebuilt package) via
/// <c>services.AddUiModule&lt;{Module}UiModule&gt;()</c>. Pure string transform
/// (no I/O), so it is unit-testable. Run <i>after</i>
/// <see cref="UiHostWiring.EnsureUiWiring"/> so the <c>AddModulusUi()</c>
/// anchor exists; when the host was never UI-wired the line still lands ahead
/// of <c>builder.Build()</c> (the <c>AddUiModule&lt;T&gt;</c> extension calls
/// <c>AddModulusUi()</c> itself).
/// </summary>
internal static class UiCrudWiring
{
    /// <summary>
    /// The whole <c>Program.cs</c> edit for <c>generate-crud --with-ui</c>: UI foundation wiring,
    /// the Tabler theme unless <paramref name="withTheme"/> is false (apps that bring their own
    /// theme pass <c>--no-theme</c>), then the module sidecar registration, then (when the page requires one) the
    /// <paramref name="permission"/> it is guarded by. Idempotent.
    /// </summary>
    public static string EnsureHostWiring(
        string content, string apiNamespace, string moduleName, bool withTheme, string? permission = null, string? permissionDescription = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var wired = UiHostWiring.EnsureUiWiring(content, UiModuleCatalog.Find("Modulus.UI.Core"));
        if (withTheme)
            wired = UiHostWiring.EnsureUiWiring(wired, UiModuleCatalog.Find(TablerThemeId));
        wired = EnsureUiModuleRegistration(wired, apiNamespace, moduleName);
        return permission is null
            ? wired
            : EnsurePagePermission(wired, moduleName, permission, permissionDescription ?? $"Use the {moduleName} admin page.");
    }

    /// <summary>
    /// The <c>Program.cs</c> half of a CRUD page's permission: the policy provider that turns <paramref name="permission"/> into a check,
    /// its declaration in the permission registry (so the Permissions UI lists it) and the Admin role's grant. The page itself carries
    /// <c>[Authorize(Policy = ...)]</c> and the nav item <c>requiredPermission</c>.
    /// </summary>
    public static string EnsurePagePermission(string content, string moduleName, string permission, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var nl = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        const string anchor = "builder.Services.AddModulusUi();";

        var lines = new List<string>();
        if (!content.Contains("AddModulusAuthorization(", StringComparison.OrdinalIgnoreCase))
            lines.Add("builder.Services.AddModulusAuthorization();");
        var needsChecker = !content.Contains(UiAccessGates.GrantCheckerCall, StringComparison.OrdinalIgnoreCase);
        if (needsChecker)
            lines.Add(UiAccessGates.GrantCheckerCall);
        if (!content.Contains($"Add(\"{permission}\"", StringComparison.OrdinalIgnoreCase))
            lines.Add($"builder.Services.AddPermissions(\"{moduleName.ToLowerInvariant()}\", permissions => permissions.Add(\"{permission}\", \"{description}\"));");
        if (!content.Contains(UiAccessGates.GrantMarker(permission), StringComparison.OrdinalIgnoreCase))
            lines.Add(UiAccessGates.GrantCall(permission));

        if (lines.Count == 0)
            return content;

        // With the UI anchor every line lands right after it (so insert in reverse to keep the order above); without one
        // (an API host) every line lands right before the host is built, which keeps the order as it is.
        if (content.Contains(anchor, StringComparison.Ordinal))
        {
            for (var i = lines.Count - 1; i >= 0; i--)
                content = InsertAfter(content, anchor, lines[i], nl);
        }
        else
        {
            foreach (var line in lines)
                content = InsertBefore(content, "var app = builder.Build();", line, nl);
        }

        var usings = new List<string> { "using Modulus.Authorization.Extensions;" };
        if (needsChecker)
            usings.Add($"using {UiAccessGates.GrantCheckerNamespace};");
        foreach (var usingLine in usings.Where(u => !content.Contains(u, StringComparison.Ordinal)))
            content = InsertUsing(content, usingLine, nl);
        return content;
    }

    /// <summary>Catalog id of the default theme installed next to the UI foundation.</summary>
    public const string TablerThemeId = "Modulus.Theme.Tabler";

    /// <summary>
    /// Returns <paramref name="content"/> with the <c>AddUiModule</c>
    /// registration plus its two usings (<c>Modulus.UI</c> for the extension,
    /// <c>{apiNamespace}.Ui</c> for the module) present exactly once.
    /// </summary>
    public static string EnsureUiModuleRegistration(string content, string apiNamespace, string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        // Match the host's prevailing line endings (Windows scaffolds CRLF).
        var nl = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var registration = $"builder.Services.AddUiModule<{moduleName}UiModule>();";
        if (!content.Contains(registration, StringComparison.Ordinal))
        {
            if (content.Contains("builder.Services.AddModulusUi();", StringComparison.Ordinal))
                content = InsertAfter(content,
                    "builder.Services.AddModulusUi();", registration, nl);
            else
                content = InsertBefore(content,
                    "var app = builder.Build();", registration, nl);
        }

        foreach (var u in new[] { "using Modulus.UI;", $"using {apiNamespace}.Ui;" })
        {
            if (!content.Contains(u, StringComparison.Ordinal))
                content = InsertUsing(content, u, nl);
        }

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

    private static string InsertAfter(string content, string anchor, string insertion, string nl)
    {
        var idx = content.IndexOf(anchor, StringComparison.Ordinal);
        if (idx < 0) return content;
        return content.Insert(idx + anchor.Length, nl + IndentOf(content, idx) + insertion);
    }

    private static string InsertBefore(string content, string anchor, string insertion, string nl)
    {
        var idx = content.IndexOf(anchor, StringComparison.Ordinal);
        if (idx < 0) return content;
        return content.Insert(LineStart(content, idx), IndentOf(content, idx) + insertion + nl);
    }

    /// <summary>The whitespace in front of the statement at <paramref name="index"/> (top-level statements have none), so an inserted line lines up with it.</summary>
    private static string IndentOf(string content, int index)
    {
        var start = LineStart(content, index);
        var indent = content[start..index];
        return indent.Trim(' ', '\t').Length == 0 ? indent : string.Empty;
    }

    private static int LineStart(string content, int index) => content.LastIndexOf('\n', Math.Max(index - 1, 0)) + 1;
}
