using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus describe [--json]</c>: what the app is made of, read from disk: kind, hosts, modules with their entities,
/// BFFs and the features wired into the hosts (auth provider, multi-tenancy, gRPC, GraphQL, webhooks, realtime, audit store,
/// AI connector). With <c>--json</c> it is the <c>result</c> of the JSON document, so a developer tool can plan its next
/// generator calls (themselves runnable with <c>--json</c> and <c>--dry-run</c>) instead of re-implementing the templates.
/// </summary>
internal sealed class DescribeCommand : Command<DescribeCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");

            var description = Describe(inventory);
            Ux.Result = description;
            Print(description);
            return 0;
        });
    }

    /// <summary>The app's description (also the <c>--json</c> result).</summary>
    internal static AppDescription Describe(ModuleDiscovery.AppInventory app)
    {
        var program = File.Exists(app.ProgramCsPath) ? File.ReadAllText(app.ProgramCsPath) : string.Empty;
        var uiProgram = File.Exists(app.UiProgramCsPath) ? File.ReadAllText(app.UiProgramCsPath) : program;

        return new AppDescription(
            Name: Path.GetFileNameWithoutExtension(app.SolutionPath),
            RootNamespace: app.RootNamespace,
            Kind: app.Kind?.Name(),
            Solution: Relative(app, app.SolutionPath),
            ApiProject: Relative(app, app.ApiProjectPath),
            WebProject: string.IsNullOrEmpty(app.WebProjectPath) ? null : Relative(app, app.WebProjectPath),
            FrameworkVersion: FrameworkVersion.Current,
            Modules: app.Modules
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => new ModuleDescription(
                    m.Name,
                    m.Namespace,
                    Relative(app, m.Directory),
                    m.DatabaseProvider,
                    MigrateSupport.IsDbshModule(CodeGen.LayerDir(m.Directory, m.Namespace, "Infrastructure")) ? "dbsh" : "efcore",
                    m.HasMigrations,
                    m.Entities.OrderBy(e => e, StringComparer.Ordinal).ToList()))
                .ToList(),
            Bffs: app.Bffs.Select(b => new BffDescription(b.Client, Relative(app, b.ProjectPath))).ToList(),
            Features: new FeatureDescription(
                Auth: AuthProviders.All.FirstOrDefault(p => p.AddMethod is not null && program.Contains(p.AddMethod + "(", StringComparison.Ordinal))?.Key ?? "none",
                MultiTenancy: program.Contains("AddMultiTenancy(", StringComparison.Ordinal),
                Permissions: UiAccessGates.HasAdminRole(program),
                Grpc: program.Contains("AddModulusGrpc(", StringComparison.Ordinal),
                GraphQL: program.Contains("AddModulusGraphQL(", StringComparison.Ordinal),
                Webhooks: program.Contains("AddModulusWebhooks(", StringComparison.Ordinal),
                Realtime: program.Contains("AddModulusRealtime(", StringComparison.Ordinal),
                AuditStore: program.Contains("AddModulusAuditStore", StringComparison.Ordinal),
                AiConnector: AiWiring.HasConnector(program),
                Ui: uiProgram.Contains("AddModulusUi(", StringComparison.Ordinal)));
    }

    private static string Relative(ModuleDiscovery.AppInventory app, string path) =>
        Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/');

    private static void Print(AppDescription d)
    {
        AnsiConsole.MarkupLine("[cyan]{0}[/] [grey]({1}, framework {2})[/]", Markup.Escape(d.Name), Markup.Escape(d.Kind ?? "unmarked"), Markup.Escape(d.FrameworkVersion));
        AnsiConsole.MarkupLine("  [grey]API host:[/] {0}", Markup.Escape(d.ApiProject));
        if (d.WebProject is not null)
            AnsiConsole.MarkupLine("  [grey]Web host:[/] {0}", Markup.Escape(d.WebProject));
        foreach (var m in d.Modules)
        {
            AnsiConsole.MarkupLine("  [grey]module[/] [cyan]{0}[/] [grey]{1}, {2}{3}[/]: {4}",
                Markup.Escape(m.Name), Markup.Escape(m.DatabaseProvider), m.MigrationEngine, m.HasMigrations ? ", migrations" : string.Empty,
                m.Entities.Count == 0 ? "[grey]no entities[/]" : Markup.Escape(string.Join(", ", m.Entities)));
        }

        foreach (var b in d.Bffs)
            AnsiConsole.MarkupLine("  [grey]bff[/] [cyan]{0}[/] [grey]{1}[/]", Markup.Escape(b.Client), Markup.Escape(b.Project));

        var f = d.Features;
        var on = new (string Name, bool On)[]
        {
            ("multi-tenancy", f.MultiTenancy), ("permissions", f.Permissions), ("grpc", f.Grpc), ("graphql", f.GraphQL),
            ("webhooks", f.Webhooks), ("realtime", f.Realtime), ("audit-store", f.AuditStore), ("ai-connector", f.AiConnector), ("ui", f.Ui),
        }.Where(x => x.On).Select(x => x.Name).ToList();
        AnsiConsole.MarkupLine("  [grey]auth:[/] {0}  [grey]features:[/] {1}", Markup.Escape(f.Auth), on.Count == 0 ? "[grey]none[/]" : string.Join(", ", on));
    }

    internal sealed record AppDescription(
        string Name,
        string RootNamespace,
        string? Kind,
        string Solution,
        string ApiProject,
        string? WebProject,
        string FrameworkVersion,
        IReadOnlyList<ModuleDescription> Modules,
        IReadOnlyList<BffDescription> Bffs,
        FeatureDescription Features);

    internal sealed record ModuleDescription(
        string Name, string Namespace, string Directory, string DatabaseProvider, string MigrationEngine, bool HasMigrations, IReadOnlyList<string> Entities);

    internal sealed record BffDescription(string Client, string Project);

    internal sealed record FeatureDescription(
        string Auth, bool MultiTenancy, bool Permissions, bool Grpc, bool GraphQL, bool Webhooks, bool Realtime, bool AuditStore, bool AiConnector, bool Ui);
}
