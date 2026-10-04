using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-audit-store</c>: keeps the business audit log and the hash-chained security audit in a database. It
/// generates the store module (<c>src/Modules/{App}.Modules.Audit/{App}.Modules.Audit.Infrastructure</c>:
/// <c>AppAuditDbContext</c>, its design-time factory and <c>AuditModule</c>) and wires the API host (the module,
/// <c>AddModulusSecurityAudit</c> when missing, the <c>Audit</c> connection string, a Development anchor file). Existing files
/// are never overwritten and edits are idempotent, so running it again changes nothing.
/// </summary>
internal sealed class AddAuditStoreCommand : Command<AddAuditStoreCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddAuditStoreCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var provider = app.Modules.Select(m => m.DatabaseProvider).FirstOrDefault(p => NewAppCommand.KnownProviders.Contains(p)) ?? "SQLite";
        var model = new AuditStoreModel { RootNamespace = app.RootNamespace, DbProvider = provider };
        var written = new List<string>();
        var hints = new List<string>();

        // ── Store module ────────────────────────────────────────────
        var infraName = $"{model.AuditNamespace}.Infrastructure";
        var infraDir = Path.Combine(app.SolutionDir, "src", "Modules", model.AuditNamespace, infraName);
        var infraCsproj = Path.Combine(infraDir, $"{infraName}.csproj");
        Write("audit/infrastructure.csproj", model, infraCsproj, written, app);
        Write("audit/AppAuditDbContext", model, Path.Combine(infraDir, "AppAuditDbContext.cs"), written, app);
        Write("audit/AppAuditDbContextFactory", model, Path.Combine(infraDir, "AppAuditDbContextFactory.cs"), written, app);
        Write("audit/AuditModule", model, Path.Combine(infraDir, "AuditModule.cs"), written, app);
        if (!Ux.DryRun)
            SolutionHelper.AddProject(app.SolutionPath, Path.GetRelativePath(app.SolutionDir, infraCsproj));

        // ── API host ────────────────────────────────────────────────
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;
        var reference = Path.GetRelativePath(apiDir, infraCsproj).Replace('\\', '/');
        Edit(app.ApiProjectPath, t => WebhooksWiring.EnsureApiProjectReference(t, reference), written, app);
        Edit(app.ProgramCsPath, t => AuditStoreWiring.EnsureApiProgram(t, model.AuditNamespace), written, app);
        var program = File.Exists(app.ProgramCsPath) ? File.ReadAllText(app.ProgramCsPath) : string.Empty;
        if (!Ux.DryRun && !program.Contains("AddModule<AuditModule>()", StringComparison.Ordinal))
            hints.Add("Program.cs: add modules.AddModule<AuditModule>() to AddModulus(...) and builder.Services.AddModulusSecurityAudit(builder.Configuration) before Build().");

        Edit(Path.Combine(apiDir, "appsettings.json"), t => AuditStoreWiring.EnsureSettings(t, "Production", model.AuditConnectionString), written, app);
        var development = Path.Combine(apiDir, "appsettings.Development.json");
        Edit(development, t => AuditStoreWiring.EnsureSettings(t, "Development", null), written, app);
        if (File.Exists(development) && !File.ReadAllText(development).Contains("AnchorFile", StringComparison.Ordinal) && !Ux.DryRun)
            hints.Add($"appsettings.Development.json already has a Security section: add \"Audit\": {{ \"AnchorFile\": \"{AuditStoreWiring.DevelopmentAnchorFile}\" }} to it.");

        AnsiConsole.MarkupLine("[green]✓[/] Audit store ({0})", Markup.Escape(provider));
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        foreach (var hint in hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        AnsiConsole.MarkupLine("  [grey]Next: modulus migrate add InitialCreate --module Audit.[/]");
        AnsiConsole.MarkupLine("  [grey]Production: set Security:Audit:AnchorFile (or register an IAuditAnchorSink) on storage the app's database role cannot reach, and make[/]");
        AnsiConsole.MarkupLine("  [grey]modulus_security_audit append-only with SecurityAuditDatabaseScripts (run as the migration role).[/]");
        return 0;
    }

    internal sealed class AuditStoreModel
    {
        public string RootNamespace { get; init; } = "";
        public string DbProvider { get; init; } = "SQLite";
        public string AuditNamespace => $"{RootNamespace}.Modules.Audit";
        public string FrameworkVersion => Services.FrameworkVersion.Current;
        public string EfProviderPackage => DbProviderInfo.Package(DbProvider);
        public string EfProviderVersion => DbProviderInfo.Version(DbProvider);
        public string UseDbMethod => DbProviderInfo.UseMethod(DbProvider);
        public string AuditConnectionString => DbProviderInfo.ConnectionString(DbProvider, "Audit");
    }

    private void Write(string template, AuditStoreModel model, string path, List<string> written, ModuleDiscovery.AppInventory app)
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
