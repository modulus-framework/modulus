namespace Modulus.Cli.Services;

/// <summary>Text edits <c>ui create-crud</c> makes to the Web host: the typed client's registration and the menu contributor's.</summary>
internal static class UiCrud
{
    /// <summary>
    /// The typed client's registration line for <c>ApiClientExtensions.cs</c>, matching how the file already authenticates: the BFF web
    /// session's token (<c>AddBffUserAccessToken</c>) or anonymous when the host has no sign-in.
    /// </summary>
    public static string ClientRegistration(string extensions, string entity)
    {
        var line = $"        services.AddModulusHttpClient<{entity}ApiClient>()";
        return extensions.Contains("AddBffUserAccessToken()", StringComparison.Ordinal)
            ? line + "\n            .AddBffUserAccessToken();\n"
            : line + ";\n";
    }

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
        return extensions[..index] + registration + nl + extensions[index..];
    }

    /// <summary>Returns <paramref name="program"/> with <c>AddTransient&lt;IMenuContributor, ...&gt;</c> before the host is built, plus its usings; unchanged when the contributor is already registered.</summary>
    public static string EnsureMenuRegistered(string program, string uiNamespace, string plural)
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

        foreach (var usingLine in new[] { "using Modulus.Ui.Abstractions.Navigation;", $"using {uiNamespace}.Menu;" })
        {
            if (program.Contains(usingLine, StringComparison.Ordinal))
                continue;
            program = usingLine + nl + program;
        }

        return program;
    }
}
