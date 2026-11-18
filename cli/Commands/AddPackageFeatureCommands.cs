using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// The shared body of the commands that wire one optional framework package into the API host (<c>add-search</c>, <c>add-storage</c>,
/// <c>add-jobs</c>, <c>add-keyvault</c>): the package reference, the service registration in <c>Configuration/FeatureServicesSetup.cs</c>
/// and the settings section. Idempotent; running it again changes nothing.
/// </summary>
internal static class FeaturePackageInstaller
{
    public static int Install(FeaturePackage feature, string startDir)
    {
        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");
        if (string.IsNullOrEmpty(app.ApiProjectPath))
            throw new InvalidOperationException("This app has no API host; run the command in an app with a `{App}.Api` project.");

        var written = new List<string>();
        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;

        if (ProjectFileService.EnsureCsprojPackageReference(app.ApiProjectPath, feature.PackageId, FrameworkVersion.Current, Ux.DryRun))
            written.Add(Path.GetRelativePath(app.SolutionDir, app.ApiProjectPath).Replace('\\', '/') + " (package reference)");

        var services = ApiHostFiles.FeatureServicesFile(app.ProgramCsPath);
        Edit(services, t => FeaturePackageWiring.EnsureServices(t, feature), written, app);
        Edit(Path.Combine(apiDir, "appsettings.json"), t => FeaturePackageWiring.EnsureSettings(t, feature, development: false), written, app);
        Edit(Path.Combine(apiDir, "appsettings.Development.json"), t => FeaturePackageWiring.EnsureSettings(t, feature, development: true), written, app);

        if (written.Count == 0)
            AnsiConsole.MarkupLine("[yellow]•[/] {0} is already wired in this app. Nothing to add.", Markup.Escape(feature.Title));
        else
            AnsiConsole.MarkupLine("[green]✓[/] {0}", Markup.Escape(feature.Title));
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        foreach (var hint in feature.Hints)
            AnsiConsole.MarkupLine("  [yellow]![/] {0}", Markup.Escape(hint));
        return 0;
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
}

/// <summary><c>modulus add-search</c>: OpenSearch or Solr behind <c>ISearchIndex</c>.</summary>
internal sealed class AddSearchCommand : Command<AddSearchCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("The search server: opensearch (default) or solr.")]
        [CommandOption("--provider")]
        public string? Provider { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => FeaturePackageInstaller.Install(
            FeaturePackageWiring.Search((s.Provider ?? "opensearch").Trim().ToLowerInvariant()), Environment.CurrentDirectory));
    }
}

/// <summary><c>modulus add-storage</c>: S3, Azure Blob Storage or Google Cloud Storage behind <c>IFileStorage</c>.</summary>
internal sealed class AddStorageCommand : Command<AddStorageCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("The cloud provider: s3 (default), azure or gcs.")]
        [CommandOption("--provider")]
        public string? Provider { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => FeaturePackageInstaller.Install(
            FeaturePackageWiring.Storage((s.Provider ?? "s3").Trim().ToLowerInvariant()), Environment.CurrentDirectory));
    }
}

/// <summary><c>modulus add-jobs</c>: the Quartz scheduler behind <c>IJobScheduler</c>.</summary>
internal sealed class AddJobsCommand : Command<AddJobsCommand.Settings>
{
    internal sealed class Settings : ModulusSettings;

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => FeaturePackageInstaller.Install(FeaturePackageWiring.Jobs(), Environment.CurrentDirectory));
    }
}

/// <summary><c>modulus add-keyvault</c>: secrets read from Azure Key Vault.</summary>
internal sealed class AddKeyVaultCommand : Command<AddKeyVaultCommand.Settings>
{
    internal sealed class Settings : ModulusSettings;

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => FeaturePackageInstaller.Install(FeaturePackageWiring.KeyVault(), Environment.CurrentDirectory));
    }
}
