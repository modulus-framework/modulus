using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus info</c> — prints an overview of the current Modulus app:
/// solution name, root namespace, host/API project, framework features
/// wired in <c>Program.cs</c>, and per-module summary. Run from inside
/// the app directory.
/// </summary>
internal sealed class InfoCommand : Command<InfoCommand.Settings>
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

            // ── Header ──────────────────────────────────────────────────
            var root = new Panel(new Rows(
                new Markup($"[cyan]{Path.GetFileNameWithoutExtension(inventory.SolutionPath)}[/]"),
                new Markup($"[grey]Solution :[/] {Path.GetFileName(inventory.SolutionPath)}"),
                new Markup($"[grey]Root ns  :[/] {inventory.RootNamespace}"),
                new Markup($"[grey]Directory:[/] {Markup.Escape(inventory.SolutionDir)}"),
                new Markup($"[grey]Host     :[/] {(inventory.ApiProjectPath.Length == 0 ? "[red]missing[/]" : Markup.Escape(Path.GetRelativePath(inventory.SolutionDir, inventory.ApiProjectPath)))}")))
                .Border(BoxBorder.Rounded)
                .Header("[yellow]Application[/]");
            AnsiConsole.Write(root);

            // ── Framework features wired in Program.cs ──────────────────
            var features = ModuleDiscovery.DetectEnabledFeatures(inventory.ProgramCsPath);
            if (features.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Framework features wired in Program.cs[/]");
                foreach (var f in features)
                    AnsiConsole.MarkupLine("  [green]✓[/] [grey]{0}[/]", f);
            }
            else if (inventory.ProgramCsPath.Length > 0)
            {
                AnsiConsole.WriteLine();
                Ux.Warning("Program.cs has no Modulus extension calls detected.");
            }

            // ── Module summary ──────────────────────────────────────────
            if (inventory.Modules.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Modules ({0})[/]", inventory.Modules.Count);
                foreach (var m in inventory.Modules.OrderBy(m => m.Name))
                {
                    var entityCount = m.Entities.Count;
                    var entityLabel = entityCount == 0
                        ? "no entities"
                        : $"{entityCount} entit{(entityCount == 1 ? "y" : "ies")} ({string.Join(", ", m.Entities)})";
                    var migrLabel = m.HasMigrations ? "[green]migrations✓[/]" : "[grey dim]no migrations[/]";
                    AnsiConsole.MarkupLine(
                        "  [cyan]{0}[/] [grey]{1} · {2} ·[/] {3}",
                        m.Name, m.DatabaseProvider, entityLabel, migrLabel);
                }
            }
            else
            {
                AnsiConsole.WriteLine();
                Ux.Warning("No modules found under src/Modules/.");
            }

            return 0;
        });
    }
}
