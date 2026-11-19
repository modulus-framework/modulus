namespace Modulus.Cli.Services;

/// <summary>
/// <c>modulus ui add-session-manager</c> on a Fluid host: the sessions Liquid page (Templates/auth/sessions.liquid), the typed client
/// that reads and ends sessions through the BFF, the endpoints that render the page, and the Program.cs lines that register and map
/// them. Each step is skipped when already done, so a second run changes nothing.
/// </summary>
internal static class UiFluidSessions
{
    public static int Run(string? output)
    {
        var start = Path.GetFullPath(output ?? "./");
        var inventory = ModuleDiscovery.Inventory(start)
            ?? throw new InvalidOperationException("No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
        var program = inventory.UiProgramCsPath;
        if (!File.Exists(program))
            throw new InvalidOperationException("The Fluid host's Program.cs was not found.");
        var programText = File.ReadAllText(program);
        if (!UiAccessGates.HasSignIn(programText) && !programText.Contains("AddWebAuthentication(", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "This Fluid host has no sign-in, so there are no sessions to manage. Generate the app with `--auth openiddict` first.");
        if (!programText.Contains("app.MapFluidAuthEndpoints(templates);", StringComparison.Ordinal))
            throw new InvalidOperationException("Program.cs has no `app.MapFluidAuthEndpoints(templates);`: map the session endpoints by hand.");

        var templates = UiTemplatePackage.Resolve()
            ?? throw new InvalidOperationException(
                $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");
        var source = Path.Combine(templates, "fluid", "account-sessions.liquid");
        if (!File.Exists(source))
            throw new InvalidOperationException(
                $"The {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package has no 'account-sessions' page for fluid. " +
                $"If it is newer than this package, set {UiTemplatePackage.VersionEnvironmentVariable} to a newer Modulus UI version.");

        var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
        var root = Path.GetFileNameWithoutExtension(inventory.UiProjectPath);
        root = root.EndsWith(".Web", StringComparison.Ordinal) ? root[..^".Web".Length] : root;

        var page = Path.Combine(uiDir, "Templates", "auth", "sessions.liquid");
        if (!File.Exists(page) || Ux.Force)
        {
            if (!Ux.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(page)!);
                File.Copy(source, page, overwrite: true);
            }
            Ux.Success("Sessions page added", Path.GetRelativePath(start, page));
        }
        else
            Ux.Info($"Already present: {Path.GetRelativePath(start, page)} (pass --force to overwrite).");

        var renderer = new TemplateEngine();
        var model = new { root_namespace = root };
        var client = Path.Combine(uiDir, "Services", "ApiClients", "SessionsApiClient.cs");
        if (!File.Exists(client) || Ux.Force)
        {
            if (!Ux.DryRun) renderer.RenderToFile("ui/SessionsFluidClient", model, client);
            Ux.Success("Client added", Path.GetRelativePath(start, client));
        }
        var endpoints = Path.Combine(uiDir, "Endpoints", "SessionsEndpoints.cs");
        if (!File.Exists(endpoints) || Ux.Force)
        {
            if (!Ux.DryRun) renderer.RenderToFile("ui/SessionsFluidEndpoints", model, endpoints);
            Ux.Success("Endpoints added", Path.GetRelativePath(start, endpoints));
        }

        var updated = EnsureProgram(programText, root);
        if (updated != programText)
        {
            if (!Ux.DryRun) File.WriteAllText(program, updated);
            Ux.Success("Web host updated", Path.GetRelativePath(start, program));
        }

        Ux.Info("The page lists the caller's sessions through the BFF (Bff:BaseUrl in the Web project's appsettings.json) and ends them with DELETE account/sessions/{id}.");
        return 0;
    }

    /// <summary>Returns Program.cs with the sessions client registered and the endpoints mapped; a line already there is not added again.</summary>
    internal static string EnsureProgram(string program, string root)
    {
        var nl = program.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var result = program;

        var usings = new[] { $"using {root}.Web.Endpoints;", $"using {root}.Web.Services.ApiClients;" }
            .Where(u => !result.Contains(u, StringComparison.Ordinal)).ToList();
        if (usings.Count > 0)
            result = string.Join(nl, usings) + nl + result;

        if (!result.Contains("AddSessionsApiClient(", StringComparison.Ordinal))
        {
            const string build = "var app = builder.Build();";
            var at = result.IndexOf(build, StringComparison.Ordinal);
            if (at < 0)
                throw new InvalidOperationException("Program.cs has no `var app = builder.Build();`: register SessionsApiClient by hand.");
            result = result.Insert(at, "builder.Services.AddSessionsApiClient(builder.Configuration);" + nl + nl);
        }

        if (!result.Contains("MapAccountSessions(", StringComparison.Ordinal))
        {
            const string auth = "app.MapFluidAuthEndpoints(templates);";
            var at = result.IndexOf(auth, StringComparison.Ordinal);
            if (at < 0)
                throw new InvalidOperationException("Program.cs has no `app.MapFluidAuthEndpoints(templates);`: map the session endpoints by hand.");
            result = result.Insert(at + auth.Length, nl + "app.MapAccountSessions(templates);");
        }
        return result;
    }
}
