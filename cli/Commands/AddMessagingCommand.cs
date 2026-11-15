using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-messaging [--transport postgresql|rabbitmq|inmemory]</c>: adds the messaging bus to the API host. It
/// references the transport's packages, registers the bus in the feature services (on PostgreSQL also the outbox and the
/// inbox), and writes the connection and transport settings. Edits are idempotent; existing files keep their other content.
/// </summary>
internal sealed class AddMessagingCommand : Command<AddMessagingCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Transport the bus sends over: postgresql (default, durable, with the transactional outbox and inbox), rabbitmq or inmemory (development only).")]
        [CommandOption("--transport")]
        public string? Transport { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal static int ExecuteCore(Settings s, string startDir)
    {
        var transport = (s.Transport ?? MessagingWiring.PostgreSql).Trim().ToLowerInvariant();
        if (!MessagingWiring.Transports.Contains(transport))
            throw new InvalidOperationException($"Unknown transport '{transport}'. Use one of: {string.Join(", ", MessagingWiring.Transports)}.");

        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ProgramCsPath))
            throw new InvalidOperationException($"Program.cs not found at {app.ProgramCsPath}.");

        var apiDir = Path.GetDirectoryName(app.ApiProjectPath)!;
        var written = new List<string>();
        var hints = new List<string>();

        Edit(app.ApiProjectPath, t => MessagingWiring.EnsurePackages(t, transport, FrameworkVersion.Current), written, app);
        Edit(ApiHostFiles.FeatureServicesFile(app.ProgramCsPath), t => MessagingWiring.EnsureApiServices(t, transport), written, app);
        Edit(Path.Combine(apiDir, "appsettings.json"), t => MessagingWiring.EnsureSettings(t, transport, app.RootNamespace, development: false), written, app);
        Edit(Path.Combine(apiDir, "appsettings.Development.json"), t => MessagingWiring.EnsureSettings(t, transport, app.RootNamespace, development: true), written, app);

        AnsiConsole.MarkupLine("[green]✓[/] Messaging over {0}", Markup.Escape(transport));
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));

        if (transport == MessagingWiring.PostgreSql)
        {
            hints.Add("Set ConnectionStrings:Messaging in the environment for production (the development file names a local database).");
            hints.Add("Publish inside a transaction: await the INpgsqlUnitOfWork.BeginAsync, publish through IBus, then CommitAsync.");
            hints.Add("The outbox and inbox tables are created on start; for a migration-owned schema, use the scripts from NpgsqlOutboxExtensions.OutboxSchemaScript() and InboxSchemaScript().");
        }
        else if (transport == MessagingWiring.RabbitMq)
            hints.Add("Set Messaging:RabbitMq:Password in the environment. Start a broker for development (docker run rabbitmq:4-management).");
        else
            hints.Add("The in-memory bus loses messages on exit: switch to postgresql or rabbitmq before production.");

        foreach (var hint in hints)
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
        written.Add(Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/'));
    }
}
