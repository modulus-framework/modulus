using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>Shared by the <c>modulus ui theme</c> commands: finds the app's UI project.</summary>
internal static class UiThemeSupport
{
    public static string ResolveUiProjectDir(string? output)
    {
        var start = Path.GetFullPath(output ?? "./");
        var inventory = ModuleDiscovery.Inventory(start)
            ?? throw new InvalidOperationException(
                "No .slnx found in the current directory tree. " +
                "Run from inside a Modulus application, or pass --output <path>.");

        if (inventory.Kind == AppKind.Api)
            throw new InvalidOperationException(
                $"This app is API-only ({AppKinds.Property}=api), so it has no UI to theme.");

        if (inventory.UiProjectPath.Length == 0 || !File.Exists(inventory.UiProjectPath))
            throw new InvalidOperationException("UI project not found. Cannot locate the app's themes.");

        return Path.GetDirectoryName(inventory.UiProjectPath)!;
    }
}

internal sealed class UiThemeCreateCommand : Command<UiThemeCreateCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Theme name (letters, digits, '-' and '_')")]
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = "";

        [Description("Base theme to start from: tabler or minimal")]
        [CommandOption("--base")]
        [DefaultValue("tabler")]
        public string Base { get; init; } = "tabler";

        [Description("Brand colour: #rrggbb or a preset (blue, indigo, purple, pink, red, orange, green, teal)")]
        [CommandOption("--colors")]
        public string? Colors { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var theme = UiThemes.Create(s.Name, s.Base, s.Colors);
            var projectDir = UiThemeSupport.ResolveUiProjectDir(s.Output);

            var manifest = UiThemes.ManifestPath(projectDir, theme.Name);
            if (File.Exists(manifest) && !Ux.Force)
                throw new InvalidOperationException(
                    $"Theme '{theme.Name}' already exists. Pass --force to overwrite it.");

            Ux.WriteFile(manifest, UiThemes.Serialize(theme));
            Ux.WriteFile(Path.Combine(Path.GetDirectoryName(manifest)!, "variables.css"), UiThemes.Export(theme, "css"));

            Ux.Success($"Theme '{theme.Name}' created", $"base {theme.Base}, primary {theme.PrimaryColor}");
            Ux.Info($"Activate it with: modulus ui theme set {theme.Name}");
            return 0;
        });
    }
}

internal sealed class UiThemeSetCommand : Command<UiThemeSetCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Name of a theme in the app's Themes folder")]
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = "";

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var projectDir = UiThemeSupport.ResolveUiProjectDir(s.Output);
            var theme = UiThemes.Load(projectDir, s.Name);

            var settingsPath = Path.Combine(projectDir, "appsettings.json");
            var current = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "{}";
            Ux.WriteFile(settingsPath, UiThemes.ApplyToAppSettings(current, theme));

            Ux.Success($"Active theme set to '{theme.Name}'", "appsettings.json: Modulus:Theme");
            return 0;
        });
    }
}

internal sealed class UiThemeListCommand : Command<UiThemeListCommand.Settings>
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
            var projectDir = UiThemeSupport.ResolveUiProjectDir(s.Output);
            var themes = UiThemes.List(projectDir);
            var active = UiThemes.ActiveTheme(Path.Combine(projectDir, "appsettings.json"));

            Ux.Result = new
            {
                active,
                themes = themes.Select(t => new { name = t.Name, @base = t.Base, primaryColor = t.PrimaryColor }).ToArray(),
                bases = UiThemes.BaseNames,
            };

            if (Ux.Json) return 0;

            if (themes.Count == 0)
            {
                Ux.Info("No themes in this app yet.");
                Ux.Info($"Create one with: modulus ui theme create <name> --base {string.Join("|", UiThemes.BaseNames)}");
                return 0;
            }

            var table = new Table().Border(TableBorder.Minimal)
                .AddColumn("[cyan]Theme[/]").AddColumn("Base").AddColumn("Primary").AddColumn("");
            foreach (var t in themes)
            {
                var isActive = string.Equals(t.Name, active, StringComparison.OrdinalIgnoreCase);
                table.AddRow(
                    Markup.Escape(t.Name),
                    Markup.Escape(t.Base),
                    $"[{t.PrimaryColor}]■[/] {t.PrimaryColor}",
                    isActive ? "[green]active[/]" : "");
            }
            AnsiConsole.Write(table);
            return 0;
        });
    }
}

internal sealed class UiThemeExportCommand : Command<UiThemeExportCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Name of a theme in the app's Themes folder")]
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = "";

        [Description("Export format: css, scss, tailwind or json")]
        [CommandOption("--format")]
        [DefaultValue("css")]
        public string Format { get; init; } = "css";

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }

        [Description("File to write (default: ./<name>.<format> in the current directory)")]
        [CommandOption("--out-file")]
        public string? OutFile { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var projectDir = UiThemeSupport.ResolveUiProjectDir(s.Output);
            var theme = UiThemes.Load(projectDir, s.Name);
            var content = UiThemes.Export(theme, s.Format);

            var path = Path.GetFullPath(s.OutFile ?? $"{theme.Name}.{UiThemes.ExportExtension(s.Format)}");
            if (File.Exists(path) && !Ux.Force)
                throw new InvalidOperationException($"{path} already exists. Pass --force to overwrite it.");

            Ux.WriteFile(path, content);
            Ux.Success($"Theme '{theme.Name}' exported", path);
            return 0;
        });
    }
}
