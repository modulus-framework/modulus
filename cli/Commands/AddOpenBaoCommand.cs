using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-openbao</c>: keeps secrets, and with <c>--tenant-databases</c> tenant database connection strings, in OpenBao. It
/// wires the API host (the OpenBao store, read-only, and the tenant-database registration), adds the <c>Synvro.Modulus.Secrets.OpenBao</c>
/// package, writes the <c>Secrets</c> settings (production and Development) and the policies and dev bootstrap under
/// <c>deploy/openbao</c>. Existing files are never overwritten and edits are idempotent, so running it again changes nothing.
/// </summary>
internal sealed class AddOpenBaoCommand : Command<AddOpenBaoCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
        [Description("How the API host logs in to OpenBao in production: kubernetes (a projected service-account token) or approle.")]
        [CommandOption("--auth")]
        public string? Auth { get; init; }

        [Description("Keep tenant database connection strings in OpenBao (AddModulusTenantDatabases). Needs a multi-tenant host with a tenant store.")]
        [CommandOption("--tenant-databases")]
        public bool TenantDatabases { get; init; }

        [Description("Generate the provisioning host (src/Provisioning): the only host with an OpenBao write identity. PostgreSQL and SQL Server.")]
        [CommandOption("--provisioner")]
        public bool Provisioner { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddOpenBaoCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var auth = (s.Auth ?? "kubernetes").Trim().ToLowerInvariant();
        if (!OpenBaoWiring.AuthMethods.Contains(auth))
            throw new ArgumentException($"--auth must be one of: {string.Join(", ", OpenBaoWiring.AuthMethods)}.");

        var written = new List<string>();
        var hints = new List<string>();
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;
        var deployDir = Path.Combine(app.SolutionDir, OpenBaoWiring.DeployDirectory.Replace('/', Path.DirectorySeparatorChar));

        // ── Deployment assets ───────────────────────────────────────
        Write("openbao/runtime_read.hcl", Path.Combine(deployDir, "runtime-read.hcl"), written, app);
        Write("openbao/provisioner.hcl", Path.Combine(deployDir, "provisioner.hcl"), written, app);
        Write("openbao/bootstrap_dev_sh", Path.Combine(deployDir, "bootstrap-dev.sh"), written, app);
        Write("openbao/docker_compose.openbao.yml", Path.Combine(app.SolutionDir, "docker-compose.openbao.yml"), written, app);

        // ── API host ────────────────────────────────────────────────
        if (ProjectFileService.EnsureCsprojPackageReference(app.ApiProjectPath, OpenBaoWiring.PackageId, FrameworkVersion.Current, Ux.DryRun))
            written.Add(Path.GetRelativePath(app.SolutionDir, app.ApiProjectPath).Replace('\\', '/') + " (package reference)");
        Edit(ApiHostFiles.FeatureServicesFile(app.ProgramCsPath), t => OpenBaoWiring.EnsureApiServices(t, s.TenantDatabases), written, app);

        var production = Path.Combine(apiDir, "appsettings.json");
        var development = Path.Combine(apiDir, "appsettings.Development.json");
        if (File.Exists(production) && OpenBaoWiring.HasOpenBaoSettings(File.ReadAllText(production)))
            hints.Add("appsettings.json already has an OpenBao section: review Secrets:OpenBao there (it was left as it is).");
        Edit(production, t => OpenBaoWiring.EnsureProductionSettings(t, auth), written, app);
        if (File.Exists(development) && OpenBaoWiring.HasOpenBaoSettings(File.ReadAllText(development)))
            hints.Add("appsettings.Development.json already has an OpenBao section: review it there (it was left as it is).");
        Edit(development, t => OpenBaoWiring.EnsureDevelopmentSettings(t), written, app);

        if (s.Provisioner)
            GenerateProvisioningHost(auth, app, written, hints);

        if (s.TenantDatabases && !ApiHostFiles.ReadAll(app.ProgramCsPath).Contains("AddEfCoreTenantStore(", StringComparison.Ordinal))
            hints.Add("--tenant-databases needs the EF tenant store (AddEfCoreTenantStore). Generate the app with --multi-tenancy, or add it before the secrets.");

        AnsiConsole.MarkupLine("[green]✓[/] OpenBao secrets ({0} login{1})", Markup.Escape(auth), s.TenantDatabases ? ", tenant databases" : string.Empty);
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        foreach (var hint in hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        AnsiConsole.MarkupLine("  [grey]Development: docker compose -f docker-compose.openbao.yml up -d, then BAO_TOKEN=dev-root deploy/openbao/bootstrap-dev.sh,[/]");
        AnsiConsole.MarkupLine("  [grey]and write the AppRole ids to {0} and {1} (both are git-ignored).[/]",
            Markup.Escape(OpenBaoWiring.DevelopmentRoleIdFile), Markup.Escape(OpenBaoWiring.DevelopmentSecretIdFile));
        AnsiConsole.MarkupLine("  [grey]Production: replace the placeholder address and mount, and apply the policies in deploy/openbao with the runtime role.[/]");
        return 0;
    }

    private void Write(string template, string path, List<string> written, ModuleDiscovery.AppInventory app)
        => Write(template, new { }, path, written, app);

    private void Write(string template, object model, string path, List<string> written, ModuleDiscovery.AppInventory app)
    {
        var display = Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/');
        if (File.Exists(path))
        {
            AnsiConsole.MarkupLine("  [yellow]•[/] [grey]{0}[/] [yellow](exists, skipped)[/]", Markup.Escape(display));
            return;
        }

        _templates.RenderToFile(template, model, path);
        written.Add(display);
    }

    // The provisioning host: a project with the write identity, its program and settings, added to the solution. Existing files are kept.
    private void GenerateProvisioningHost(string auth, ModuleDiscovery.AppInventory app, List<string> written, List<string> hints)
    {
        // The provider package is referenced by the module projects that hold the DbContexts, so every project of the solution is read.
        var projects = string.Concat(Directory.EnumerateFiles(app.SolutionDir, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        var engine = ProvisioningHost.DetectEngine(projects)
            ?? throw new InvalidOperationException("The provisioning host needs a database package in the API host (PostgreSQL or SQL Server).");
        ProvisioningHost.EnsureSupported(engine);

        var model = new ProvisioningHost.Model(app.RootNamespace, FrameworkVersion.Current, engine, auth, Guid.NewGuid().ToString("D"));
        var directory = Path.Combine(app.SolutionDir, ProvisioningHost.ProjectDirectory(app.RootNamespace).Replace('/', Path.DirectorySeparatorChar));
        Write("provisioning/provisioning.csproj", model, Path.Combine(directory, $"{app.RootNamespace}.Provisioning.csproj"), written, app);
        Write("provisioning/Program", model, Path.Combine(directory, "Program.cs"), written, app);
        Write("provisioning/appsettings", model, Path.Combine(directory, "appsettings.json"), written, app);
        Write("provisioning/appsettings.Development", model, Path.Combine(directory, "appsettings.Development.json"), written, app);

        if (File.Exists(app.SolutionPath))
            SolutionHelper.AddProject(app.SolutionPath, ProvisioningHost.ProjectFile(app.RootNamespace));

        hints.Add("Provisioning host: the API must issue unencrypted access tokens (Identity:EncryptAccessTokens=false), or this host cannot read them.");
        hints.Add($"Provisioning host: replace the Tenancy and Authorization connection strings (the Authorization one must be the API host's database) and the Provisioning:Authority and Audience placeholders. Set the API host's Identity:Audiences to the same audience, so its access tokens carry it. Grant tenancy:data-stores:manage to the administrators in that database.");
        hints.Add("Provisioning host (development): write the provisioner AppRole ids from bootstrap-dev.sh to secrets/openbao-provisioner-role-id and secrets/openbao-provisioner-secret-id (git-ignored).");
    }

    // Applies a pure text edit; true when the file changed (and was written, unless --dry-run).
    private static bool Edit(string path, Func<string, string> edit, List<string> written, ModuleDiscovery.AppInventory app)
    {
        if (!File.Exists(path))
            return false;
        var before = File.ReadAllText(path);
        var after = edit(before);
        if (after == before)
            return false;
        Ux.WriteFile(path, after);
        written.Add(Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/') + " (updated)");
        return true;
    }
}
