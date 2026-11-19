using System.Text.RegularExpressions;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// One optional framework feature that keeps its data in a database of its own and so needs a store module with a <c>DbContext</c>:
/// the generated <c>{App}.Modules.{Key}.Infrastructure</c> project, the host's module registration and services, and the settings.
/// </summary>
internal sealed record StoreModuleSpec(
    string Key,
    string Title,
    string PackageId,
    string ContextNamespace,
    string ContextBase,
    string StoreCall,
    string[] ServiceLines,
    string[] HostUsings,
    string? EndpointLine,
    string? EndpointUsing,
    string[] Hints)
{
    public static readonly StoreModuleSpec Media = new("Media", "Media library", "Synvro.Modulus.Media.EntityFrameworkCore",
        "Modulus.Media.EntityFrameworkCore", "ModulusMediaDbContext", "AddModulusMediaStore",
        ["builder.Services.AddModulusMedia();"], ["using Modulus.Media;"], "app.MapModulusMedia();", "using Modulus.Media;",
        ["Uploads are stored through IFileStorage (AddFileStorage); metadata is tenant-scoped. Endpoints are under /api/media."]);

    public static readonly StoreModuleSpec Notifications = new("Notifications", "Notification inbox", "Synvro.Modulus.Notifications.EntityFrameworkCore",
        "Modulus.Notifications.EntityFrameworkCore", "ModulusNotificationsDbContext", "AddModulusNotificationsStore",
        ["builder.Services.AddModulusNotifications();"], ["using Modulus.Notifications;"], "app.MapModulusNotifications();", "using Modulus.Notifications;",
        ["The inbox and channel preferences are served under /api/notifications."]);

    public static readonly StoreModuleSpec Localization = new("Localization", "Runtime translations", "Synvro.Modulus.Localization.EntityFrameworkCore",
        "Modulus.Localization.EntityFrameworkCore", "ModulusLocalizationDbContext", "AddModulusEfLocalizationStore",
        [], [], null, null,
        ["Translations are read from the database instead of memory; edit them at run time through ILocalizationStore."]);
}

/// <summary>The shared body of <c>add-media</c>, <c>add-notifications</c> and <c>add-localization</c>. Existing files are never overwritten and edits are idempotent.</summary>
internal static class StoreModuleInstaller
{
    private static readonly TemplateEngine Templates = new();

    public static int Install(StoreModuleSpec spec, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath) || string.IsNullOrEmpty(app.ApiProjectPath))
            throw new InvalidOperationException("This app has no API host; run the command in an app with a `{App}.Api` project.");

        var provider = app.Modules.Select(m => m.DatabaseProvider).FirstOrDefault(p => NewAppCommand.KnownProviders.Contains(p)) ?? "SQLite";
        var storeNamespace = $"{app.RootNamespace}.Modules.{spec.Key}";
        var model = new StoreModel
        {
            Key = spec.Key,
            Title = spec.Title,
            PackageId = spec.PackageId,
            ContextNamespace = spec.ContextNamespace,
            ContextBase = spec.ContextBase,
            StoreCall = spec.StoreCall,
            StoreNamespace = storeNamespace,
            DbProvider = provider,
            TargetFramework = AppFramework.Resolve(app.ProgramCsPath),
        };

        var written = new List<string>();
        var infraName = $"{storeNamespace}.Infrastructure";
        var infraDir = Path.Combine(app.SolutionDir, "src", "Modules", storeNamespace, infraName);
        var infraCsproj = Path.Combine(infraDir, $"{infraName}.csproj");
        Write("store/infrastructure.csproj", model, infraCsproj, written, app);
        Write("store/StoreDbContext", model, Path.Combine(infraDir, $"App{spec.Key}DbContext.cs"), written, app);
        Write("store/StoreDbContextFactory", model, Path.Combine(infraDir, $"App{spec.Key}DbContextFactory.cs"), written, app);
        Write("store/StoreModule", model, Path.Combine(infraDir, $"{spec.Key}Module.cs"), written, app);
        if (!Ux.DryRun)
            SolutionHelper.AddProject(app.SolutionPath, Path.GetRelativePath(app.SolutionDir, infraCsproj));

        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;
        var reference = Path.GetRelativePath(apiDir, infraCsproj).Replace('\\', '/');
        Edit(app.ApiProjectPath, t => WebhooksWiring.EnsureApiProjectReference(t, reference), written, app);
        Edit(ApiHostFiles.ModulesFile(app.ProgramCsPath),
            t => WebhooksWiring.EnsureModuleRegistration(t, $"{spec.Key}Module", $"{infraName}"), written, app);
        if (spec.ServiceLines.Length > 0)
            Edit(ApiHostFiles.FeatureServicesFile(app.ProgramCsPath), t => EnsureServices(t, spec), written, app);
        if (spec.EndpointLine is not null)
            Edit(ApiHostFiles.FeatureEndpointsFile(app.ProgramCsPath), t => EnsureEndpoint(t, spec), written, app);

        var connection = DbProviderInfo.ConnectionString(provider, spec.Key);
        Edit(Path.Combine(apiDir, "appsettings.json"), t => EnsureConnection(t, spec.Key, connection), written, app);

        AnsiConsole.MarkupLine("[green]✓[/] {0}", Markup.Escape(spec.Title));
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        AnsiConsole.MarkupLine("  [yellow]![/] Author its schema: modulus migrate add InitialCreate --module {0}", Markup.Escape(spec.Key));
        foreach (var hint in spec.Hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        return 0;
    }

    private static string EnsureServices(string text, StoreModuleSpec spec)
    {
        var nl = WebhooksWiring.NewLine(text);
        if (!text.Contains(spec.ServiceLines[0], StringComparison.Ordinal))
        {
            var block = $"        // ── {spec.Title} ──────────────────────────────────────────{nl}"
                + string.Concat(spec.ServiceLines.Select(l => "        " + l + nl));
            var inserted = ApiHostFiles.InsertFeatureServices(text, block, nl);
            if (inserted == text)
                return text;
            text = inserted + nl;
        }

        foreach (var u in spec.HostUsings)
            text = WebhooksWiring.EnsureUsing(text, u);
        return text;
    }

    private static string EnsureEndpoint(string text, StoreModuleSpec spec)
    {
        var nl = WebhooksWiring.NewLine(text);
        if (!text.Contains(spec.EndpointLine!, StringComparison.Ordinal))
        {
            var inserted = ApiHostFiles.InsertFeatureEndpoints(text, "        " + spec.EndpointLine + nl, nl);
            if (inserted is null)
                return text;
            text = inserted;
        }

        return spec.EndpointUsing is null ? text : WebhooksWiring.EnsureUsing(text, spec.EndpointUsing);
    }

    /// <summary>Adds <c>ConnectionStrings:{key}</c> when the file has a <c>ConnectionStrings</c> section without it (text edit).</summary>
    internal static string EnsureConnection(string json, string key, string connection)
    {
        if (Regex.IsMatch(json, $"\"{key}\"\\s*:"))
            return json;
        var section = Regex.Match(json, "\"ConnectionStrings\"\\s*:\\s*\\{");
        if (!section.Success)
            return WebhooksWiring.EnsureTopLevelSection(json, "ConnectionStrings", [$"\"{key}\": \"{connection.Replace("\\", "\\\\")}\""]);
        var nl = WebhooksWiring.NewLine(json);
        var line = $"{nl}    \"{key}\": \"{connection.Replace("\\", "\\\\")}\",";
        return json.Insert(section.Index + section.Length, line);
    }

    private static void Write(string template, StoreModel model, string path, List<string> written, ModuleDiscovery.AppInventory app)
    {
        var display = Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/');
        if (File.Exists(path))
        {
            AnsiConsole.MarkupLine("  [yellow]•[/] [grey]{0}[/] [yellow](exists, skipped)[/]", Markup.Escape(display));
            return;
        }

        Templates.RenderToFile(template, model, path);
        written.Add(display);
    }

    private static void Edit(string path, Func<string, string> edit, List<string> written, ModuleDiscovery.AppInventory app)
    {
        if (!File.Exists(path))
            return;
        var before = File.ReadAllText(path);
        var after = edit(before);
        if (after == before)
            return;
        Ux.WriteFile(path, after);
        written.Add(Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/') + " (updated)");
    }

    internal sealed class StoreModel
    {
        public string Key { get; init; } = "";
        public string Title { get; init; } = "";
        public string PackageId { get; init; } = "";
        public string ContextNamespace { get; init; } = "";
        public string ContextBase { get; init; } = "";
        public string StoreCall { get; init; } = "";
        public string StoreNamespace { get; init; } = "";
        public string DbProvider { get; init; } = "SQLite";
        public string TargetFramework { get; init; } = AppFramework.Default;
        public string FrameworkVersion => Services.FrameworkVersion.Current;
        public string EfProviderPackage => DbProviderInfo.Package(DbProvider);
        public string EfProviderVersion => DbProviderInfo.Version(DbProvider);
        public string UseDbMethod => DbProviderInfo.UseMethod(DbProvider);
        public string ConnectionString => DbProviderInfo.ConnectionString(DbProvider, Key);
    }
}

internal sealed class AddMediaCommand : Command<AddMediaCommand.Settings>
{
    internal sealed class Settings : ModulusSettings;

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => StoreModuleInstaller.Install(StoreModuleSpec.Media, Environment.CurrentDirectory));
    }
}

internal sealed class AddNotificationsCommand : Command<AddNotificationsCommand.Settings>
{
    internal sealed class Settings : ModulusSettings;

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => StoreModuleInstaller.Install(StoreModuleSpec.Notifications, Environment.CurrentDirectory));
    }
}

internal sealed class AddLocalizationCommand : Command<AddLocalizationCommand.Settings>
{
    internal sealed class Settings : ModulusSettings;

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => StoreModuleInstaller.Install(StoreModuleSpec.Localization, Environment.CurrentDirectory));
    }
}
