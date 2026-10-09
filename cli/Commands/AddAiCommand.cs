using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-ai</c>: hosts the AI platform's connector wire contract v1 in the API host. It adds the
/// <c>Cobytelabs.Modulus.AI.Connector</c> packages, wires <c>AddModulusAiConnector</c> (envelope users matched to the local
/// identity accounts when the host has them, plus the EF change journal) and <c>MapModulusAiConnector</c>, seeds the
/// <c>Ai:Connector</c> settings (no key, no instance: nothing can call it until the platform is registered), gives the tests a
/// hashed test key and writes <c>AiConnectorTests</c> and <c>AiConformanceTests</c> (the platform's conformance suite
/// against a fake platform, package <c>Cobytelabs.Modulus.AI.Connector.Testing</c>). Idempotent. Mark entities with <c>generate-crud &lt;Entity&gt; --ai</c>.
/// </summary>
internal sealed class AddAiCommand : Command<AddAiCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
        [System.ComponentModel.Description("Also scaffold the migration that adds the ai_changes journal table to every module (modulus migrate add AddAiChanges). Needs the dotnet-ef tool.")]
        [CommandOption("--migrate")]
        public bool Migrate { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddAiCommand().ExecuteCore(Environment.CurrentDirectory, s.Migrate));
    }

    internal int ExecuteCore(string startDir, bool migrate = false)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (app.Kind == AppKind.WebApp)
            throw new InvalidOperationException("A web app has no API host for the connector. Use an api or webapp+api app.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var program = File.ReadAllText(app.ProgramCsPath);
        var written = new List<string>();
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;

        foreach (var package in new[] { AiWiring.ConnectorPackageId, AiWiring.ConnectorEfPackageId })
        {
            if (ProjectFileService.EnsureCsprojPackageReference(app.ApiProjectPath, package, FrameworkVersion.Current, Ux.DryRun))
                written.Add(Path.GetRelativePath(app.SolutionDir, app.ApiProjectPath).Replace('\\', '/') + " (updated)");
        }

        Edit(app.ProgramCsPath, AiWiring.EnsureConnectorProgram, written, app);

        // The journal table must reach the migrations the EF tools scaffold: each module's design-time factory passes the contributor.
        var journalModules = AiWiring.JournalModules(app.SolutionDir);
        foreach (var module in journalModules)
        {
            if (ProjectFileService.EnsureCsprojPackageReference(module.InfrastructureCsproj, AiWiring.ConnectorEfPackageId, FrameworkVersion.Current, Ux.DryRun))
                written.Add(Path.GetRelativePath(app.SolutionDir, module.InfrastructureCsproj).Replace('\\', '/') + " (updated)");
            Edit(module.Factory, AiWiring.EnsureDesignTimeJournal, written, app);
        }

        Edit(Path.Combine(apiDir, "appsettings.json"), t => AiWiring.EnsureConnectorSettings(t, app.RootNamespace), written, app);

        var testsDir = Path.Combine(app.SolutionDir, "tests", $"{app.RootNamespace}.Tests");
        if (Directory.Exists(testsDir))
        {
            var testingSettings = Path.Combine(apiDir, "appsettings.Testing.json");
            if (!File.Exists(testingSettings))
            {
                Ux.WriteFile(testingSettings, "{\n}\n");
                written.Add(Path.GetRelativePath(app.SolutionDir, testingSettings).Replace('\\', '/'));
            }

            Edit(testingSettings, AiWiring.EnsureConnectorTestingSettings, written, app);

            var testsProject = Path.Combine(testsDir, $"{app.RootNamespace}.Tests.csproj");
            if (File.Exists(testsProject)
                && ProjectFileService.EnsureCsprojPackageReference(testsProject, AiWiring.ConnectorTestingPackageId, FrameworkVersion.Current, Ux.DryRun))
                written.Add(Path.GetRelativePath(app.SolutionDir, testsProject).Replace('\\', '/') + " (updated)");

            var model = Model(app, program);
            Write("ai/AiConnectorTests", model, Path.Combine(testsDir, "AiConnectorTests.cs"), written, app);
            Write("ai/AiConformanceTests", model, Path.Combine(testsDir, "AiConformanceTests.cs"), written, app);
        }

        AnsiConsole.MarkupLine("[green]✓[/] AI connector ([grey]/_ai/connector/*[/])");
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        if (!AiWiring.HasIdentityUsers(program))
            AnsiConsole.MarkupLine("  [yellow]![/] No local accounts: register an IAiConnectorUserResolver (ai.UseUserResolver<T>()), or every user call is refused.");
        AnsiConsole.MarkupLine("  [grey]Expose data: modulus generate-crud <Entity> --ai (capabilities, record lookups, the index).[/]");
        AnsiConsole.MarkupLine("  [grey]Register the app with the platform, then set Ai:Connector:Instances, Platform:Issuer/JwksUrl/BaseUrl and the hash of the platform's key[/]");
        AnsiConsole.MarkupLine("  [grey](AiApiKeys.Hash); the platform's own key for revocations (Ai:Connector:Platform:ApiKey) goes in user secrets or a vault.[/]");
        if (!migrate)
        {
            AnsiConsole.MarkupLine("  [grey]The ai_changes journal is a new table in every module database: modulus add-ai --migrate (or migrate add AddAiChanges).[/]");
            return 0;
        }

        // The journal table joins every module's model: scaffold the migration now (needs the project to build and dotnet-ef).
        var journalNames = journalModules.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return MigrateAddCommand.Run(new MigrateAddCommand.Settings { Name = "AddAiChanges", Output = app.SolutionDir }, m => journalNames.Contains(m.Name));
    }

    internal static AiTestModel Model(ModuleDiscovery.AppInventory app, string program) => new()
    {
        RootNamespace = app.RootNamespace,
        HasAdminRole = UiAccessGates.HasAdminRole(program),
        HasLocalAccounts = AiWiring.HasIdentityUsers(program),
        MultiTenant = program.Contains("AddMultiTenancy(", StringComparison.Ordinal),
    };

    internal sealed class AiTestModel
    {
        public string RootNamespace { get; init; } = "";
        public bool HasAdminRole { get; init; }
        public bool HasLocalAccounts { get; init; }
        public bool MultiTenant { get; init; }
        public string AdminRole => UiAccessGates.AdminRole;
        public string TestApiKey => AiWiring.TestApiKey;
    }

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

    // Applies a pure text edit; true when the file changed (and was written, unless --dry-run).
    internal static bool Edit(string path, Func<string, string> edit, List<string> written, ModuleDiscovery.AppInventory app)
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
