using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus ui diff [target]</c> — compares the app's overrides of framework views (components, feature UI pages, theme layouts; ejected with
/// <c>modulus ui eject</c> or written by hand) with the framework's current views, and says whether the app customized them, the framework changed since, or both.
/// <c>--check</c> exits 1 when a view needs attention (framework changed since the eject), for CI.
/// </summary>
internal sealed class UiDiffCommand : Command<UiDiffCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Only this component, feature UI, theme or view, e.g. Card, Users, Users/Details (default: every override in the app).")]
        [CommandArgument(0, "[target]")]
        public string? Target { get; init; }

        [Description("List each override's status without printing the line diffs.")]
        [CommandOption("--summary")]
        [DefaultValue(false)]
        public bool Summary { get; init; }

        [Description("Exit with code 1 when the framework changed since an override was ejected (Outdated or Conflict).")]
        [CommandOption("--check")]
        [DefaultValue(false)]
        public bool Check { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var apiDir = UiEject.ResolveApiDir(Path.GetFullPath(s.Output ?? "./"));

            // A typo in the target throws (listing what exists) instead of reporting "nothing overridden".
            var diffs = UiDiff.Compare(apiDir, s.Target);
            if (diffs.Count == 0)
            {
                Ux.Info("No framework views are overridden in this app. Use `modulus ui eject <component | feature UI | theme>` to take ownership of one.");
                return 0;
            }

            foreach (var diff in diffs)
            {
                var relative = Path.GetRelativePath(apiDir, diff.Path);
                AnsiConsole.MarkupLine("{0} [cyan]{1}[/] [grey]{2}[/]", Badge(diff.Status), Markup.Escape(diff.Framework.Target), Markup.Escape(relative));
                if (diff.Status != UiViewStatus.Identical)
                {
                    AnsiConsole.MarkupLine("  [grey]{0}[/]", Markup.Escape(Explain(diff)));
                }

                if (!s.Summary)
                {
                    foreach (var line in diff.Diff)
                    {
                        var color = line.StartsWith('-') ? "red" : line.StartsWith('+') ? "green" : "grey";
                        AnsiConsole.MarkupLine("    [{0}]{1}[/]", color, Markup.Escape(line));
                    }
                }
            }

            var needsAttention = diffs.Count(d => d.Status is UiViewStatus.Outdated or UiViewStatus.Conflict);
            if (diffs.Any(d => d.Status == UiViewStatus.Identical))
            {
                Ux.Info("Identical overrides change nothing: delete the file to go back to the framework's view.");
            }

            AnsiConsole.MarkupLine(
                "{0} override(s): {1} identical, {2} customized, {3} outdated, {4} conflicting, {5} unmarked.",
                diffs.Count,
                diffs.Count(d => d.Status == UiViewStatus.Identical),
                diffs.Count(d => d.Status == UiViewStatus.Customized),
                diffs.Count(d => d.Status == UiViewStatus.Outdated),
                diffs.Count(d => d.Status == UiViewStatus.Conflict),
                diffs.Count(d => d.Status == UiViewStatus.Unmarked));

            return s.Check && needsAttention > 0 ? 1 : 0;
        });
    }

    private static string Badge(UiViewStatus status) => status switch
    {
        UiViewStatus.Identical => "[green]identical [/]",
        UiViewStatus.Customized => "[blue]customized[/]",
        UiViewStatus.Outdated => "[yellow]outdated   [/]",
        UiViewStatus.Conflict => "[red]conflict   [/]",
        _ => "[grey]unmarked  [/]",
    };

    private static string Explain(UiViewDiff diff) => diff.Status switch
    {
        UiViewStatus.Identical => "Same as the framework's current view; the override changes nothing and can be deleted.",
        UiViewStatus.Customized => $"Your changes only (ejected from framework {diff.EjectedFrom}); the framework's view is unchanged since.",
        UiViewStatus.Outdated => $"Unchanged by you, but the framework's view changed since it was ejected ({diff.EjectedFrom}). " +
            "Run `modulus ui eject <target> --force` to take the update.",
        UiViewStatus.Conflict => $"You changed it and the framework's view changed since it was ejected ({diff.EjectedFrom}). Merge the framework's changes by hand.",
        _ => "No eject marker, so there is no way to tell whether the framework changed. Showing how it differs from the current framework view.",
    };
}
