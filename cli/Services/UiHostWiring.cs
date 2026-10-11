namespace Modulus.Cli.Services;

/// <summary>
/// The edits the <c>ui</c> commands make to an existing Web host: routing for the MVC controllers they write, and (on a split
/// app) the forwarding controller that lets the UI package's pages reach the API's identity endpoints. Every edit is idempotent
/// and works on the text, so the comments of the generated files survive.
/// </summary>
internal static class UiHostWiring
{
    /// <summary>Route prefixes the forwarding controller carries to the API host.</summary>
    public const string ProxyControllerFile = "IdentityApiProxy.cs";

    /// <summary>The anonymous recovery routes the forwarding controller opens (listed in the Web host's loosening allowlist).</summary>
    public static readonly IReadOnlyList<(string Route, string Reason)> AnonymousRoutes =
    [
        ("/account/forgot-password", "Password recovery page (forwarded to the API)"),
        ("/account/reset-password", "Password reset page (forwarded to the API)"),
    ];

    /// <summary>
    /// Makes the Web host route its MVC controllers: <c>AddControllersWithViews()</c> before the app is built and
    /// <c>MapControllers()</c> before it runs. Returns the lines it changed (empty when it was already wired).
    /// </summary>
    public static IReadOnlyList<string> EnsureControllerRouting(string programPath)
    {
        var text = File.ReadAllText(programPath);
        var changed = new List<string>();
        if (!text.Contains("AddControllersWithViews(") && !text.Contains("AddControllers("))
        {
            text = InsertBefore(text, "var app = builder.Build();", "builder.Services.AddControllersWithViews();");
            changed.Add("AddControllersWithViews()");
        }

        if (!text.Contains("MapControllers("))
        {
            text = InsertBefore(text, "app.Run();", "app.MapControllers();");
            changed.Add("MapControllers()");
        }

        if (changed.Count > 0)
            File.WriteAllText(programPath, text);
        return changed;
    }

    /// <summary>
    /// Wires the forwarding controller into a split Web host: writes <see cref="ProxyControllerFile"/> into <c>Controllers/</c>,
    /// adds <c>AddIdentityApiProxy()</c> to Program.cs and opens the two anonymous recovery routes in the loosening allowlist.
    /// </summary>
    public static IReadOnlyList<string> EnsureIdentityProxy(string webDir, string programPath, string rootNamespace, TemplateEngine renderer)
    {
        var changed = new List<string>();
        var controllerDir = Path.Combine(webDir, "Controllers");
        var proxyFile = Path.Combine(controllerDir, ProxyControllerFile);
        if (!File.Exists(proxyFile))
        {
            renderer.RenderToFile("ui/IdentityApiProxy", new { Namespace = rootNamespace }, proxyFile);
            changed.Add($"Controllers/{ProxyControllerFile}");
        }

        var text = File.ReadAllText(programPath);
        if (!text.Contains("AddIdentityApiProxy("))
        {
            text = InsertBefore(text, "var app = builder.Build();", "builder.Services.AddIdentityApiProxy();");
            File.WriteAllText(programPath, text);
            changed.Add("AddIdentityApiProxy()");
        }

        var allowlist = Path.Combine(webDir, "security", "loosening-allowlist.json");
        if (File.Exists(allowlist) && AddAllowlistEntries(allowlist))
            changed.Add("security/loosening-allowlist.json");

        return changed;
    }

    /// <summary>
    /// Adds the anonymous recovery routes to the allowlist's <c>endpoints</c> array, skipping any already listed. The file is
    /// edited as text because it carries comments. Returns true when it changed.
    /// </summary>
    internal static bool AddAllowlistEntries(string path)
    {
        var text = File.ReadAllText(path);
        var key = text.IndexOf("\"endpoints\"", StringComparison.Ordinal);
        if (key < 0)
            return false;
        var close = ClosingBracket(text, text.IndexOf('[', key));
        if (close < 0)
            return false;

        var entries = new List<string>();
        foreach (var (route, reason) in AnonymousRoutes)
        {
            if (text.Contains($"\"route\": \"{route}\"", StringComparison.Ordinal))
                continue;
            entries.Add($"\n    {{ \"route\": \"{route}\", \"methods\": [\"POST\"], \"reason\": \"{reason}\" }}");
        }

        if (entries.Count == 0)
            return false;

        // Insert after the last non-space character before the array's closing bracket, adding a comma when the array is not empty.
        var last = close - 1;
        while (last > key && char.IsWhiteSpace(text[last]))
            last--;
        var separator = text[last] == '[' ? string.Empty : ",";
        text = text.Insert(last + 1, separator + string.Join(",", entries));
        File.WriteAllText(path, text);
        return true;
    }

    /// <summary>The index of the bracket that closes the array opened at <paramref name="open"/>, skipping strings; -1 when unclosed.</summary>
    private static int ClosingBracket(string text, int open)
    {
        if (open < 0)
            return -1;
        var depth = 0;
        var inString = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
                inString = true;
            else if (c == '[')
                depth++;
            else if (c == ']' && --depth == 0)
                return i;
        }

        return -1;
    }

    private static string InsertBefore(string text, string anchor, string line)
    {
        var index = text.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException($"Program.cs has no '{anchor}'; add the line '{line}' by hand.");
        var lineStart = text.LastIndexOf('\n', index) + 1;
        var indent = text[lineStart..index];
        return text.Insert(lineStart, indent + line + Environment.NewLine);
    }
}
