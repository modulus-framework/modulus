using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiAddChartCommand : Command<UiAddChartCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Chart type: " + UiCharts.TypeList)]
        [CommandOption("--type")]
        [DefaultValue("line")]
        public string Type { get; init; } = "line";

        [Description("UI engine: mvc, razor-pages or blazor (default: the one recorded in .modulus.json)")]
        [CommandOption("--engine")]
        public string? Engine { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var chart = UiCharts.Find(s.Type);
            return UiPageScaffold.Run([chart.Page], "ui/ChartsController", "ChartsController.cs",
                s.Engine, s.Output, $"a {chart.Key} chart", folder: "Charts", sharedController: true);
        });
    }
}
