using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiAddAuthCommand : Command<UiAddAuthCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("UI engine: mvc, razor-pages, blazor or fluid (default: the one recorded in .modulus.json)")]
        [CommandOption("--engine")]
        public string? Engine { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => IsFluid(s) ? UiFluidAuth.Run(s.Output) : UiPageScaffold.Run(
            UiPageScaffold.Recovery, "ui/AuthRecoveryController", "AuthRecoveryController.cs", s.Engine, s.Output, "password recovery pages",
            callsApi: true, anonymous: true, requiresSignIn: true));
    }

    private static bool IsFluid(Settings s)
    {
        var inventory = ModuleDiscovery.Inventory(Path.GetFullPath(s.Output ?? "./"));
        return inventory is not null && UiScaffold.ResolveEngine(s.Engine, inventory.SolutionDir) == "fluid";
    }
}

internal sealed class UiAdd2FaCommand : Command<UiAdd2FaCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("UI engine: mvc, razor-pages, blazor or fluid (default: the one recorded in .modulus.json)")]
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
            UiPageScaffold.TwoFactor, "ui/TwoFactorController", "TwoFactorController.cs", s.Engine, s.Output, "the two-factor page",
            callsApi: true, requiresSignIn: true));
    }
}

internal sealed class UiAddSessionManagerCommand : Command<UiAddSessionManagerCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("UI engine: mvc, razor-pages, blazor or fluid (default: the one recorded in .modulus.json)")]
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
            var inventory = ModuleDiscovery.Inventory(Path.GetFullPath(s.Output ?? "./"));
            if (inventory is not null && UiScaffold.ResolveEngine(s.Engine, inventory.SolutionDir) == "fluid")
                return UiFluidSessions.Run(s.Output);
            return UiPageScaffold.Run(
                UiPageScaffold.Sessions, "ui/SessionsController", "SessionsController.cs", s.Engine, s.Output, "the session manager",
                callsApi: true, requiresSignIn: true);
        });
    }
}
