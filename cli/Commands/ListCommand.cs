using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus list</c> — lists every business module under
/// <c>src/Modules</c> with its database provider, entities (DbSet&lt;T&gt;
/// declarations), and migration status. Run from inside a Modulus app.
/// </summary>
internal sealed class ListCommand : Command<ListCommand.Settings>
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
                    "No .slnx found in the current directory tree. " +
                    "Run from inside a Modulus application, or pass --output <path>.");

            if (inventory.Modules.Count == 0)
            {
                Ux.Warning("No modules found under src/Modules/.");
                Ux.Info($"Add one with: modulus add-module <Name>");
                return 0;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[cyan]Modules in " + Path.GetFileName(inventory.SolutionPath) + "[/]")
                .AddColumn("[cyan]Module[/]")
                .AddColumn("[grey]Provider[/]")
                .AddColumn("[grey]Entities[/]")
                .AddColumn("[grey]Migrations[/]");

            foreach (var m in inventory.Modules.OrderBy(m => m.Name))
            {
                var entities = m.Entities.Count == 0
                    ? "[grey dim]—[/]"
                    : string.Join(", ", m.Entities);
                var migrations = m.HasMigrations ? "[green]yes[/]" : "[grey dim]no[/]";
                table.AddRow(
                    $"[cyan]{m.Name}[/]",
                    $"[grey]{m.DatabaseProvider}[/]",
                    entities,
                    migrations);
            }

            AnsiConsole.Write(table);
            return 0;
        });
    }
}
