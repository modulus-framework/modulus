namespace Modulus.Cli.Services;

/// <summary>
/// <c>modulus ui add-auth</c> on a Fluid host: the forgot-password and reset-password Liquid pages (copied from the template package into
/// <c>Templates/auth</c>), the endpoints that render them and call the API's <c>/account/*</c> routes, the typed client, the Program.cs
/// wiring and the loosening-allowlist entries. Each step is skipped when already done, so a second run changes nothing.
/// </summary>
internal static class UiFluidAuth
{
    private static readonly string[] Pages = ["forgot-password", "reset-password"];

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
                "This Fluid host has no sign-in, so password recovery has nothing to recover. Generate the app with `--auth openiddict` first.");

        var templates = UiTemplatePackage.Resolve()
            ?? throw new InvalidOperationException(
                $"Could not find the {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package. " +
                $"Pack it into ~/.modulus/feed (or set {UiTemplatePackage.FeedEnvironmentVariable}), or wait for it on nuget.org.");
        var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
        var root = Path.GetFileNameWithoutExtension(inventory.UiProjectPath);
        root = root.EndsWith(".Web", StringComparison.Ordinal) ? root[..^".Web".Length] : root;

        var sources = Pages.Select(p => (Name: p, Source: Path.Combine(templates, "fluid", $"auth-{p}.liquid"))).ToList();
        foreach (var (name, source) in sources)
            if (!File.Exists(source))
                throw new InvalidOperationException(
                    $"The {UiTemplatePackage.PackageId} {UiTemplatePackage.DefaultVersion} package has no 'auth-{name}' page for fluid. " +
                    $"If it is newer than this package, set {UiTemplatePackage.VersionEnvironmentVariable} to a newer Modulus UI version.");

        foreach (var (name, source) in sources)
        {
            var target = Path.Combine(uiDir, "Templates", "auth", $"{name}.liquid");
            if (File.Exists(target) && !Ux.Force)
            {
                Ux.Info($"Already present: {Path.GetRelativePath(start, target)} (pass --force to overwrite).");
                continue;
            }
            if (!Ux.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            Ux.Success($"{name} page added", Path.GetRelativePath(start, target));
        }

        var renderer = new TemplateEngine();
        var model = new { root_namespace = root };
        var endpoints = Path.Combine(uiDir, "Endpoints", "AccountRecoveryEndpoints.cs");
        var client = Path.Combine(uiDir, "Services", "ApiClients", "AccountApiClient.cs");
        if (!File.Exists(endpoints) || Ux.Force)
        {
            renderer.RenderToFile("ui/AccountFluidEndpoints", model, endpoints);
            Ux.Success("Endpoints added", Path.GetRelativePath(start, endpoints));
        }
        if (!File.Exists(client) || Ux.Force)
        {
            renderer.RenderToFile("app/AccountApiClient.Web", model, client);
            Ux.Success("Client added", Path.GetRelativePath(start, client));
        }

        var updated = EnsureProgram(programText, root);
        if (updated != programText)
        {
            if (!Ux.DryRun) File.WriteAllText(program, updated);
            Ux.Success("Web host updated", Path.GetRelativePath(start, program));
        }

        var allowlist = Path.Combine(uiDir, "Security", "loosening-allowlist.json");
        if (File.Exists(allowlist))
        {
            var routes = new[] { "/auth/forgot-password", "/auth/reset-password", "/ResetPassword" }
                .Select(r => (r, (string?)null, "Password recovery: anonymous by design, the API answers the same for every address"));
            if (!Ux.DryRun && UiHostWiring.AddAllowlistEntries(allowlist, routes))
                Ux.Success("Web host updated", Path.GetRelativePath(start, allowlist));
        }
        else
            Ux.Warning("No Security/loosening-allowlist.json in the Web project: add the three recovery routes to it, or the host refuses to start outside Development.");

        Ux.Info("The pages call POST account/forgot-password and account/reset-password on the API (Api:BaseUrl in the Web project's appsettings.json).");
        return 0;
    }

    /// <summary>Returns Program.cs with the recovery client registered and the endpoints mapped; a line already there is not added again.</summary>
    internal static string EnsureProgram(string program, string root)
    {
        var nl = program.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var result = program;

        var usings = new[] { "using Modulus.Platform.Http;", $"using {root}.Web.Endpoints;", $"using {root}.Web.Services.ApiClients;" }
            .Where(u => !result.Contains(u, StringComparison.Ordinal)).ToList();
        if (usings.Count > 0)
            result = string.Join(nl, usings) + nl + result;

        if (!result.Contains("AddModulusHttpClient<AccountApiClient>", StringComparison.Ordinal))
        {
            const string build = "var app = builder.Build();";
            var at = result.IndexOf(build, StringComparison.Ordinal);
            if (at < 0)
                throw new InvalidOperationException("Program.cs has no `var app = builder.Build();`: register AccountApiClient by hand.");
            result = result.Insert(at,
                "builder.Services.AddModulusHttpClient<AccountApiClient>().ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false });" + nl + nl);
        }

        if (!result.Contains("MapAccountRecovery(", StringComparison.Ordinal))
        {
            const string auth = "app.MapFluidAuthEndpoints(templates);";
            var at = result.IndexOf(auth, StringComparison.Ordinal);
            if (at < 0)
                throw new InvalidOperationException("Program.cs has no `app.MapFluidAuthEndpoints(templates);`: map the recovery endpoints by hand.");
            result = result.Insert(at + auth.Length, nl + "app.MapAccountRecovery(templates);");
        }
        return result;
    }
}
