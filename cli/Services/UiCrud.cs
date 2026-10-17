namespace Modulus.Cli.Services;

/// <summary>Text edits <c>ui create-crud</c> makes to the Web host: the typed client's registration and the menu contributor's.</summary>
internal static class UiCrud
{
    /// <summary>
    /// The typed client's registration line for <c>ApiClientExtensions.cs</c>, matching how the file already reaches the API: through
    /// the BFF gateway (<c>AddBffGatewayClient</c>) when the host has a sign-in, or anonymous when it has none.
    /// </summary>
    public static string ClientRegistration(string extensions, string entity)
        => extensions.Contains("AddBffGatewayClient<", StringComparison.Ordinal)
            ? $"        services.AddBffGatewayClient<{entity}ApiClient>();\n"
            : $"        services.AddModulusHttpClient<{entity}ApiClient>();\n";

    /// <summary>Returns <paramref name="extensions"/> with the client registered before <c>return services;</c>, or unchanged when it is there or the anchor is missing.</summary>
    public static string EnsureClientRegistered(string extensions, string entity)
    {
        if (extensions.Contains($"<{entity}ApiClient>", StringComparison.Ordinal))
            return extensions;

        const string anchor = "        return services;";
        var index = extensions.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
            return extensions;

        var nl = extensions.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var registration = ClientRegistration(extensions, entity).Replace("\n", nl);
        var updated = extensions[..index] + registration + nl + extensions[index..];
        return WithApiClientsUsing(updated, nl);
    }

    /// <summary>
    /// Adds <c>using {namespace}.ApiClients;</c> before the file's namespace when it is missing. The app template leaves it out
    /// until the first client exists (an app created with --no-example has no ApiClients namespace yet).
    /// </summary>
    private static string WithApiClientsUsing(string extensions, string nl)
    {
        var at = extensions.IndexOf("namespace ", StringComparison.Ordinal);
        var end = at < 0 ? -1 : extensions.IndexOf(';', at);
        if (end < 0) return extensions;

        var usingLine = $"using {extensions[(at + "namespace ".Length)..end].Trim()}.ApiClients;";
        if (extensions.Contains(usingLine, StringComparison.Ordinal)) return extensions;
        return extensions[..at] + usingLine + nl + extensions[at..];
    }

    /// <summary>
    /// Whether the Web host finds its menu contributors itself (<c>AddMenuContributors()</c> scans <c>Components/Navigation/Menu</c>),
    /// so a new contributor file is all a menu entry needs.
    /// </summary>
    public static bool HostScansMenu(string program) => program.Contains("AddMenuContributors()", StringComparison.Ordinal);

    /// <summary>
    /// Returns <paramref name="program"/> with <c>AddTransient&lt;IMenuContributor, ...&gt;</c> before the host is built, plus its usings; unchanged when the contributor is already registered.
    /// For a host without the menu scan (<see cref="HostScansMenu"/>), <paramref name="menuNamespace"/> is where the contributor lives.
    /// </summary>
    public static string EnsureMenuRegistered(string program, string menuNamespace, string plural)
    {
        var registration = $"builder.Services.AddTransient<IMenuContributor, {plural}MenuContributor>();";
        if (program.Contains($"{plural}MenuContributor", StringComparison.Ordinal))
            return program;

        const string build = "var app = builder.Build();";
        var at = program.IndexOf(build, StringComparison.Ordinal);
        if (at < 0)
            return program;

        var nl = program.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lineStart = program.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1;
        program = program.Insert(lineStart, registration + nl + nl);

        foreach (var usingLine in new[] { "using Modulus.Ui.Abstractions.Navigation;", $"using {menuNamespace};" })
        {
            if (program.Contains(usingLine, StringComparison.Ordinal))
                continue;
            program = usingLine + nl + program;
        }

        return program;
    }
}
