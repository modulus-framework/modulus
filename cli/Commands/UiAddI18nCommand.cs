using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiAddI18nCommand : Command<UiAddI18nCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Culture names, comma separated; the first is the default (e.g. en,es,fr-FR)")]
        [CommandOption("--languages")]
        public string? Languages { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var cultures = UiI18n.ParseLanguages(s.Languages);

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
            if (inventory.Kind == AppKind.Api)
                throw new InvalidOperationException($"This app is API-only ({AppKinds.Property}=api), so it has no UI to translate.");
            if (inventory.UiProjectPath.Length == 0 || !File.Exists(inventory.UiProjectPath))
                throw new InvalidOperationException("UI project not found. Cannot locate appsettings.json.");

            var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
            var settingsPath = Path.Combine(uiDir, "appsettings.json");
            var current = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "{}";
            var settings = UiI18n.ApplyToAppSettings(current, cultures);

            // Check the pipeline before writing anything, so a refusal leaves the app untouched.
            var programPath = inventory.UiProgramCsPath;
            string? program = null;
            if (programPath.Length > 0 && File.Exists(programPath))
                program = UiI18n.EnsureLocalization(File.ReadAllText(programPath));

            Ux.WriteFile(settingsPath, settings);
            Ux.Success($"Languages set: {string.Join(", ", cultures)}", "appsettings.json: Modulus:Theme:Cultures");

            if (program is null)
            {
                Ux.Warning("Could not find app.UseRouting() in Program.cs. Add app.UseModulusLocalization(); before it " +
                           "(Modulus.AspNetCore.Mvc) so the request culture follows the language picker.");
            }
            else if (program != File.ReadAllText(programPath))
            {
                Ux.WriteFile(programPath, program);
                Ux.Success("Added app.UseModulusLocalization() to Program.cs");
            }

            if (cultures.Count == 1)
                Ux.Info("One language only: add a second to show the language picker in the theme settings.");
            return 0;
        });
    }
}
