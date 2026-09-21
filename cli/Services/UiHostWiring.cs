namespace Modulus.Cli.Services;

/// <summary>
/// Idempotent <c>Program.cs</c> surgery shared by <c>modulus app --ui</c> and
/// <c>modulus ui add</c>. Pure string transform (no I/O, no processes), so it
/// is unit-testable. Ensures, in pipeline order:
/// <list type="bullet">
/// <item>services: <c>AddModulusLocalization</c> → <c>AddModulusUi</c> →
/// <c>AddRazorPages</c> → module <c>Add…</c> call → the backend services its pages need, each on its
/// own line;</item>
/// <item>pipeline: <c>UseStaticFiles</c> → <c>MapRazorPages</c> → module
/// <c>Map…</c> call → <c>MapModulusUiMenu</c>, ahead of <c>app.Run()</c>.</item>
/// </list>
/// Replaces two near-identical private implementations that both missed
/// <c>AddRazorPages</c> (<c>MapRazorPages</c> without it throws at startup)
/// and anchored the localization insert inside the <c>AddModulus(</c> call's
/// parens, generating uncompilable code.
/// </summary>
internal static class UiHostWiring
{
    /// <summary>
    /// Returns <paramref name="content"/> with every UI wiring for
    /// <paramref name="module"/> present exactly once (existing lines are
    /// left untouched; running twice is a no-op).
    /// </summary>
    public static string EnsureUiWiring(string content, UiModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(module);

        // Localization + UI extensions live in Platform / UI packages;
        // the host needs the namespaces even when transitively referenced.
        content = EnsureUsing(content, "Modulus.Localization");
        // AddModulusUi / MapModulusUiMenu live in Modulus.UI, and every module's Add…/Map… extensions
        // in its own namespace (the theme's differs from its package name: ExtensionNamespace).
        // Without these the wired Program.cs does not compile.
        content = EnsureUsing(content, "Modulus.UI");
        content = EnsureUsing(content, module.ExtensionNamespace ?? module.Namespace);

        // ── Services ──────────────────────────────────────────────
        // Anchor on the statement AFTER the AddModulus block so insertions
        // land on their own lines (never inside the AddModulus(…) parens).
        if (!content.Contains("AddModulusLocalization", StringComparison.OrdinalIgnoreCase))
            content = InsertBeforeService(content, "builder.Services.AddModulusLocalization();");

        if (!content.Contains("AddModulusUi(", StringComparison.OrdinalIgnoreCase))
            content = InsertAfter(content,
                "builder.Services.AddModulusLocalization();",
                "builder.Services.AddModulusUi();");

        if (!content.Contains("AddRazorPages", StringComparison.OrdinalIgnoreCase))
            content = InsertAfter(content,
                "builder.Services.AddControllers();",
                "builder.Services.AddRazorPages();",
                fallbackAnchor: "builder.Services.AddModulusUi();");

        if (!module.IsCore && !content.Contains(module.AddMethod, StringComparison.OrdinalIgnoreCase))
            content = InsertAfter(content,
                "builder.Services.AddModulusUi();",
                $"builder.Services.{module.AddMethod}(builder.Configuration);");

        // The services the module's pages resolve (IFileStorage, ISettingManager, …); without them the page
        // 500s on first request. Every one is TryAdd, so order against the module's Add… call is irrelevant.
        // A registration the host already has (even a provider-specific one) is left alone.
        foreach (var backend in module.BackendRegistrations ?? [])
        {
            if (content.Contains(backend.Marker, StringComparison.OrdinalIgnoreCase))
                continue;

            content = EnsureUsing(content, backend.Namespace);
            content = InsertAfter(content, "builder.Services.AddModulusUi();", backend.Call);
        }

        content = EnsureAccessGate(content, module);

        // ── Pipeline ──────────────────────────────────────────────
        if (!content.Contains("MapRazorPages", StringComparison.OrdinalIgnoreCase))
            content = InsertBefore(content, "app.Run();", "app.MapRazorPages();");

        if (!content.Contains("UseStaticFiles", StringComparison.OrdinalIgnoreCase))
            content = InsertBefore(content, "app.MapRazorPages", "app.UseStaticFiles();",
                fallbackAnchor: "app.Run();");

        if (!module.IsCore && module.HasEndpoints)
        {
            var mapCall = $"app.{module.AddMethod.Replace("Add", "Map")}();";
            if (!content.Contains(mapCall, StringComparison.OrdinalIgnoreCase))
                content = InsertBefore(content, "app.Run();", mapCall);
        }

        if (!content.Contains("MapModulusUiMenu", StringComparison.OrdinalIgnoreCase))
            content = InsertBefore(content, "app.Run();", "app.MapModulusUiMenu();");

        return content;
    }

    /// <summary>
    /// The <c>Program.cs</c> half of <see cref="UiAccessGates"/>: the permission policy provider and the Admin role's grant of the
    /// UI's permission (the <c>appsettings.json</c> half is <see cref="UiAccessGates.WriteSettings"/>). Nothing for an ungated UI or a host with no sign-in.
    /// </summary>
    private static string EnsureAccessGate(string content, UiModuleDefinition module)
    {
        if (!UiAccessGates.Applies(content, module) || module.Gate is not { } gate)
            return content;

        content = EnsureUsing(content, "Modulus.Authorization.Extensions");

        if (!content.Contains("AddModulusAuthorization(", StringComparison.OrdinalIgnoreCase))
            content = InsertAfter(content, "builder.Services.AddModulusUi();", "builder.Services.AddModulusAuthorization();");

        // The menu asks ICurrentUser.HasPermission; without this it reads permission claims, which a cookie sign-in does not carry,
        // so every permission-gated item (this UI's own included) would be hidden from the administrator too.
        if (!content.Contains(UiAccessGates.GrantCheckerCall, StringComparison.OrdinalIgnoreCase))
        {
            content = EnsureUsing(content, UiAccessGates.GrantCheckerNamespace);
            content = InsertAfter(content, "builder.Services.AddModulusUi();", UiAccessGates.GrantCheckerCall);
        }

        if (!content.Contains(UiAccessGates.GrantMarker(gate), StringComparison.OrdinalIgnoreCase))
            content = InsertAfter(content, "builder.Services.AddModulusUi();", UiAccessGates.GrantCall(gate));

        return content;
    }

    /// <summary>
    /// Ensures a top-level <c>using</c> for <paramref name="ns"/> exists
    /// exactly once (appended after the last using, or prepended when the
    /// file has none). CRLF-safe: reuses the file's dominant newline.
    /// </summary>
    private static string EnsureUsing(string content, string ns)
    {
        if (content.Contains($"using {ns};", StringComparison.Ordinal))
            return content;

        var newline = NewlineOf(content);
        var insertion = $"using {ns};{newline}";

        var lastUsing = content.LastIndexOf(
            $"{newline}using ", StringComparison.Ordinal);
        var lineStart = lastUsing >= 0
            ? lastUsing + newline.Length
            : content.StartsWith("using ", StringComparison.Ordinal) ? 0 : -1;
        if (lineStart < 0)
            return insertion + content;

        var lineEnd = content.IndexOf(newline, lineStart, StringComparison.Ordinal);
        return lineEnd < 0
            ? content + newline + insertion.TrimEnd()
            : content.Insert(lineEnd + newline.Length, insertion);
    }

    /// <summary>
    /// Inserts a service registration ahead of the post-<c>AddModulus</c>
    /// statements (primary anchor), falling back to just before
    /// <c>builder.Build()</c> for hand-written hosts.
    /// </summary>
    private static string InsertBeforeService(string content, string insertion)
    {
        if (content.Contains("builder.Services.AddModulusExceptionHandling();", StringComparison.OrdinalIgnoreCase))
            return InsertBefore(content, "builder.Services.AddModulusExceptionHandling();", insertion);

        return InsertBefore(content, "var app = builder.Build();", insertion);
    }

    private static string InsertAfter(string content, string anchor, string insertion, string? fallbackAnchor = null)
    {
        var idx = content.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && fallbackAnchor is not null)
        {
            idx = content.IndexOf(fallbackAnchor, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return content;
            var fallbackEnd = idx + fallbackAnchor.Length;
            return content.Insert(fallbackEnd, NewlineOf(content) + IndentOf(content, idx) + insertion);
        }

        if (idx < 0) return content;
        var insertPos = idx + anchor.Length;
        return content.Insert(insertPos, NewlineOf(content) + IndentOf(content, idx) + insertion);
    }

    private static string InsertBefore(string content, string anchor, string insertion, string? fallbackAnchor = null)
    {
        var idx = content.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && fallbackAnchor is not null)
            idx = content.IndexOf(fallbackAnchor, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content;
        return content.Insert(LineStart(content, idx), IndentOf(content, idx) + insertion + NewlineOf(content));
    }

    /// <summary>The whitespace in front of the statement at <paramref name="index"/> (top-level statements have none), so an inserted line lines up with it.</summary>
    private static string IndentOf(string content, int index)
    {
        var start = LineStart(content, index);
        var indent = content[start..index];
        return indent.Trim(' ', '\t').Length == 0 ? indent : string.Empty;
    }

    private static int LineStart(string content, int index) => content.LastIndexOf('\n', Math.Max(index - 1, 0)) + 1;

    /// <summary>
    /// The file's own line ending. Inserting <see cref="Environment.NewLine"/> instead put CRLFs into
    /// LF files on Windows; a later pass then detected CRLF and mis-anchored its <c>using</c> insert.
    /// </summary>
    private static string NewlineOf(string content)
        => content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
