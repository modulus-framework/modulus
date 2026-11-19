using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-authorization-store</c>: moves an existing app's authorization store from the framework's default context to
/// the application's own, so its schema is authored with migrations (production refuses to create it without them). It
/// generates the Authorization module (<c>src/Modules/{App}.Modules.Authorization/{App}.Modules.Authorization.Infrastructure</c>:
/// <c>AppAuthorizationDbContext</c>, its design-time factory and <c>AuthorizationStoreRegistration</c>), references it from the
/// API host and replaces the host's registration. Existing files are never overwritten; running it again changes nothing.
/// </summary>
internal sealed class AddAuthorizationStoreCommand : Command<AddAuthorizationStoreCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddAuthorizationStoreCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var host = ApiHostFiles.ReadAll(app.ProgramCsPath);
        if (host.Contains(AuthorizationStoreWiring.OwnStoreMarker, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine("[green]✓[/] The authorization store is already the application's own (AddAuthorizationStore); nothing to do.");
            return 0;
        }
        if (!AuthorizationStoreWiring.UsesDefaultStore(host))
            throw new InvalidOperationException(
                "This host registers no authorization store (generated with --no-example, or not an API host), so there is nothing to move.");

        var provider = app.Modules.Select(m => m.DatabaseProvider).FirstOrDefault(p => NewAppCommand.KnownProviders.Contains(p)) ?? "SQLite";
        var model = new AuthorizationStoreModel { RootNamespace = app.RootNamespace, DbProvider = provider, TargetFramework = AppFramework.Resolve(app.ProgramCsPath) };
        var written = new List<string>();
        var hints = new List<string>();

        // ── Store module ────────────────────────────────────────────
        var infraName = $"{model.AuthorizationNamespace}.Infrastructure";
        var infraDir = Path.Combine(app.SolutionDir, "src", "Modules", model.AuthorizationNamespace, infraName);
        var infraCsproj = Path.Combine(infraDir, $"{infraName}.csproj");
        Write("authorization/infrastructure.csproj", model, infraCsproj, written, app);
        Write("authorization/AppAuthorizationDbContext", model, Path.Combine(infraDir, "AppAuthorizationDbContext.cs"), written, app);
        Write("authorization/AppAuthorizationDbContextFactory", model, Path.Combine(infraDir, "AppAuthorizationDbContextFactory.cs"), written, app);
        Write("authorization/AuthorizationStoreRegistration", model, Path.Combine(infraDir, "AuthorizationStoreRegistration.cs"), written, app);
        if (!Ux.DryRun)
            SolutionHelper.AddProject(app.SolutionPath, Path.GetRelativePath(app.SolutionDir, infraCsproj));

        // ── API host ────────────────────────────────────────────────
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;
        var reference = Path.GetRelativePath(apiDir, infraCsproj).Replace('\\', '/');
        Edit(app.ApiProjectPath, t => WebhooksWiring.EnsureApiProjectReference(t, reference), written, app);
        var registrationFile = ApiHostFiles.FileWith(app.ProgramCsPath, AuthorizationStoreWiring.DefaultRegistrationMarker);
        Edit(registrationFile, t => AuthorizationStoreWiring.ReplaceRegistration(t, model.AuthorizationNamespace), written, app);

        AnsiConsole.MarkupLine("[green]✓[/] Authorization store moved to the application's own context ({0})", Markup.Escape(provider));
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        hints.Add("A database that already has the schema (EnsureCreated in Development, or an earlier run) must not get the new InitialCreate migration applied on top: delete it in Development, or in production mark InitialCreate as applied in __EFMigrationsHistory after checking the schema matches.");
        hints.Add("Run modulus migrate add InitialCreate --module Authorization, then modulus migrate update.");
        foreach (var hint in hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        return 0;
    }

    internal sealed class AuthorizationStoreModel
    {
        public string RootNamespace { get; init; } = "";
        public string DbProvider { get; init; } = "SQLite";
        public string TargetFramework { get; init; } = AppFramework.Default;
        public string AuthorizationNamespace => $"{RootNamespace}.Modules.Authorization";
        public string FrameworkVersion => Services.FrameworkVersion.Current;
        public string EfProviderPackage => DbProviderInfo.Package(DbProvider);
        public string EfProviderVersion => DbProviderInfo.Version(DbProvider);
        public string UseDbMethod => DbProviderInfo.UseMethod(DbProvider);
        public string AuthorizationConnectionString => DbProviderInfo.ConnectionString(DbProvider, "Authorization");
    }

    private void Write(string template, AuthorizationStoreModel model, string path, List<string> written, ModuleDiscovery.AppInventory app)
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
