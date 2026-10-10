using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiAddAuthCommand : Command<UiAddAuthCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
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
        return CommandRunner.Run(() => UiPageScaffold.Run(
            UiPageScaffold.Recovery, "ui/AuthRecoveryController", "AuthRecoveryController.cs", s.Engine, s.Output, "password recovery pages"));
    }
}

internal sealed class UiAdd2FaCommand : Command<UiAdd2FaCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
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
        return CommandRunner.Run(() => UiPageScaffold.Run(
            UiPageScaffold.TwoFactor, "ui/TwoFactorController", "TwoFactorController.cs", s.Engine, s.Output, "the two-factor page"));
    }
}

internal sealed class UiAddSessionManagerCommand : Command<UiAddSessionManagerCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
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
        return CommandRunner.Run(() => UiPageScaffold.Run(
            UiPageScaffold.Sessions, "ui/SessionsController", "SessionsController.cs", s.Engine, s.Output, "the session manager"));
    }
}
