using System.Text.Json;

namespace Modulus.Cli.Services;

/// <summary>A dashboard-style page the <c>Modulus.Ui.Templates</c> package can scaffold.</summary>
internal sealed record UiPageTemplate(string Key, string TemplateName, string Title, string Description);

/// <summary>Shared by the commands that scaffold pages from the template package: engine, template and output-path rules.</summary>
internal static class UiScaffold
{
    public static readonly string[] Engines = ["mvc", "razor-pages", "blazor"];

    public static readonly IReadOnlyList<UiPageTemplate> Dashboards =
    [
        new("overview", "dashboard", "Overview", "Admin overview: user, module and session counts."),
        new("analytics", "analytics", "Analytics", "Key metrics, traffic and top pages."),
        new("reports", "reports-dashboard", "Reports", "Report counts, schedule and recent runs."),
        new("audit", "audit-logs", "Audit logs", "Searchable list of audit events."),
    ];

    /// <summary>The engine recorded in <c>.modulus.json</c> (written by <c>modulus app</c>); "none" when absent or unreadable.</summary>
    public static string ReadEngine(string solutionDir)
    {
        var path = Path.Combine(solutionDir, ".modulus.json");
        if (!File.Exists(path)) return "none";
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.TryGetProperty("ui_engine", out var e) ? e.GetString() ?? "none" : "none";
        }
        catch (JsonException) { return "none"; }
    }

    public static string ResolveEngine(string? requested, string solutionDir)
    {
        var engine = (requested ?? ReadEngine(solutionDir)).ToLowerInvariant();
        if (engine == "fluid")
            throw new InvalidOperationException(
                "The Fluid templates can't be scaffolded yet (their Liquid placeholders clash with the template syntax). Use mvc, razor-pages or blazor.");
        if (!Engines.Contains(engine))
            throw new InvalidOperationException(
                engine == "none"
                    ? $"No UI engine found. Pass --engine {string.Join("|", Engines)}, or create the app with --ui-engine."
                    : $"Unknown UI engine '{engine}'. Choose one of: {string.Join(", ", Engines)}.");
        return engine;
    }

    public static UiPageTemplate FindDashboard(string key) =>
        Dashboards.FirstOrDefault(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"Unknown dashboard '{key}'. Choose one of: {string.Join(", ", Dashboards.Select(d => d.Key))}.");

    /// <summary>Path of a template inside the unpacked package, or null when the engine has no such template.</summary>
    public static string? TemplateFile(string templatesDir, string engine, string templateName)
    {
        var ext = engine == "blazor" ? "razor" : "cshtml";
        var file = Path.Combine(templatesDir, engine, $"{templateName}.{ext}.sbn");
        return File.Exists(file) ? file : null;
    }

    /// <summary>Where the page goes in the UI project: Views/{Module}, Pages/{Module} or Components/Pages/{Module}.</summary>
    public static string OutputPath(string engine, string uiProjectDir, string module, string pageName) => engine switch
    {
        "mvc" => Path.Combine(uiProjectDir, "Views", module, $"{pageName}.cshtml"),
        "razor-pages" => Path.Combine(uiProjectDir, "Pages", module, $"{pageName}.cshtml"),
        "blazor" => Path.Combine(uiProjectDir, "Components", "Pages", module, $"{pageName}.razor"),
        _ => throw new ArgumentException($"Unknown UI engine '{engine}'."),
    };
}
