using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiAuditCommand : Command<UiAuditCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("WCAG level to check: A, AA or AAA (every rule so far is level A)")]
        [CommandOption("--level")]
        [DefaultValue("AA")]
        public string Level { get; init; } = "AA";

        [Description("Write a report as json, markdown or html (next to --out-file)")]
        [CommandOption("--format")]
        public string? Format { get; init; }

        [Description("Report file (default: accessibility-audit.<format> in the current directory)")]
        [CommandOption("--out-file")]
        public string? OutFile { get; init; }

        [Description("Exit with code 1 when issues are found (for CI)")]
        [CommandOption("--fail-on-issues")]
        [DefaultValue(false)]
        public bool FailOnIssues { get; init; }

        [Description("App root directory (default: current directory); the scan covers its src folder")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            UiAudit.LevelRank(s.Level);
            if (s.Format is not null) UiAudit.Report([], 0, s.Level, s.Format); // validates the format before scanning

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
            var src = Path.Combine(inventory.SolutionDir, "src");
            if (!Directory.Exists(src)) src = inventory.SolutionDir;

            var files = UiAudit.FindFiles(src);
            var issues = UiAudit.ForLevel(UiAudit.ScanFiles(inventory.SolutionDir, files), s.Level);

            Ux.Result = new
            {
                level = s.Level, filesScanned = files.Count, issueCount = issues.Count,
                issues = issues.Select(i => new { rule = i.Rule, file = i.File, line = i.Line, snippet = i.Snippet }),
            };

            if (!Ux.Json) PrintSummary(issues, files.Count, s.Level);

            if (s.Format is not null)
            {
                var path = Path.GetFullPath(s.OutFile ?? $"accessibility-audit.{UiAudit.ExtensionOf(s.Format)}");
                Ux.WriteFile(path, UiAudit.Report(issues, files.Count, s.Level, s.Format));
                Ux.Success("Report written", path);
            }

            return s.FailOnIssues && issues.Count > 0 ? 1 : 0;
        });
    }

    private static void PrintSummary(IReadOnlyList<AuditIssue> issues, int files, string level)
    {
        if (issues.Count == 0)
        {
            Ux.Success($"No issues found in {files} files (level {level})", "markup scan only; contrast and focus order need a browser");
            return;
        }

        var table = new Table().Border(TableBorder.Minimal)
            .AddColumn("[cyan]Rule[/]").AddColumn("Issue").AddColumn("WCAG").AddColumn("Count");
        foreach (var group in issues.GroupBy(i => i.Rule).OrderBy(g => g.Key))
        {
            var rule = UiAudit.RuleOf(group.Key);
            table.AddRow(rule.Id, Markup.Escape(rule.Title), $"{rule.Criterion} ({rule.Level})", group.Count().ToString());
        }
        AnsiConsole.Write(table);

        foreach (var i in issues.Take(15))
            AnsiConsole.MarkupLine("  [grey]{0}:{1}[/] {2} [grey]{3}[/]", Markup.Escape(i.File), i.Line, i.Rule, Markup.Escape(i.Snippet));
        if (issues.Count > 15)
            Ux.Info($"…and {issues.Count - 15} more; use --format markdown for the full list");
        Ux.Warning($"{issues.Count} issue{(issues.Count == 1 ? "" : "s")} in {files} files (level {level})");
    }
}
