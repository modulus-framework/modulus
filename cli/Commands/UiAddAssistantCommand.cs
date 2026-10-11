using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus ui add-assistant</c>: hosts the AI platform's embedded assistant in the Web app: the <c>POST /ai/session</c> endpoint
/// (mints the platform session token with the server-side API key, behind sign-in and the <c>ai:use</c> permission), a layout partial that
/// renders the SDK's <c>&lt;ai-assistant&gt;</c> element, and the <c>Ai:Host</c> settings. Razor Pages and MVC layouts.
/// </summary>
internal sealed class UiAddAssistantCommand : Command<UiAddAssistantCommand.Settings>
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
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");
            if (inventory.Kind != AppKind.WebAppApi)
                throw new InvalidOperationException(
                    $"The assistant needs a web app (create the app with --kind webapp+api); this one is {(inventory.Kind?.Label() ?? "not marked with an app kind")}.");
            if (UiScaffold.ResolveEngine(null, inventory.SolutionDir) == "blazor")
                throw new InvalidOperationException("The assistant partial is for Razor Pages and MVC layouts. In Blazor, add the <ai-assistant> element to your layout yourself.");

            var uiDir = Path.GetDirectoryName(inventory.UiProjectPath)!;
            var uiNamespace = Path.GetFileNameWithoutExtension(inventory.UiProjectPath);
            var program = inventory.UiProgramCsPath;

            var endpoint = Path.Combine(uiDir, "Ai", "AiAssistant.cs");
            var partial = Path.Combine(uiDir, "Pages", "Shared", "_AiAssistant.cshtml");
            var existing = new[] { endpoint, partial }.Where(File.Exists).ToList();
            if (existing.Count > 0 && !Ux.Force)
                throw new InvalidOperationException(
                    $"Already present: {string.Join(", ", existing.Select(f => Path.GetRelativePath(start, f)))}. Pass --force to overwrite.");

            // Check the edits before writing anything, so a refusal leaves the app untouched.
            var programText = UiAssistant.EnsureProgram(File.ReadAllText(program), uiNamespace);
            var settingsPath = Path.Combine(uiDir, "appsettings.json");
            var settingsText = UiAssistant.EnsureSettings(File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "{}");

            var renderer = new TemplateEngine();
            var model = new { UiNamespace = uiNamespace };
            renderer.RenderToFile("ui/AiAssistant", model, endpoint);
            Ux.Success("Session endpoint added", Path.GetRelativePath(start, endpoint));
            renderer.RenderToFile("ui/AiAssistantPartial", model, partial);
            Ux.Success("Assistant partial added", Path.GetRelativePath(start, partial));

            Edit(program, programText, start);
            Edit(settingsPath, settingsText, start);

            var layout = new[] { "Pages/Shared/_Layout.cshtml", "Views/Shared/_Layout.cshtml" }
                .Select(p => Path.Combine(uiDir, p)).FirstOrDefault(File.Exists);
            var layoutText = layout is null ? null : UiAssistant.EnsureLayout(File.ReadAllText(layout));
            if (layout is not null && layoutText is not null)
                Edit(layout, layoutText, start);
            else
                Ux.Warning($"No layout with </body> found: add {UiAssistant.PartialCall} to your layout.");

            // Declare the permission on the API host when it uses the permission registry.
            var apiProgram = inventory.ProgramCsPath;
            if (File.Exists(apiProgram))
                Edit(apiProgram, ApiPermissionWiring.EnsurePermission(File.ReadAllText(apiProgram), "ai", UiAssistant.Permission, "Use the AI assistant"), start);

            Ux.Info("Set Ai:Host:Enabled=true and Ai:Host:BaseUrl, then store the key: dotnet user-secrets set Ai:Host:ApiKey <key>. " +
                    "Copy the platform SDK script to wwwroot/lib/ai-sdk/ai-assistant.js (self-hosted, so the CSP stays clean) and allow the gateway in connect-src.");
            return 0;
        });
    }

    private static void Edit(string path, string updated, string start)
    {
        if (File.Exists(path) && File.ReadAllText(path) == updated)
            return;
        Ux.WriteFile(path, updated);
        Ux.Success("Updated", Path.GetRelativePath(start, path));
    }
}
