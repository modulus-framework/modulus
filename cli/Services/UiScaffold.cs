using System.Text.Json;

namespace Modulus.Cli.Services;

/// <summary>A dashboard-style page the <c>Modulus.Ui.Templates</c> package can scaffold.</summary>
internal sealed record UiPageTemplate(string Key, string TemplateName, string Title, string Description);

/// <summary>Shared by the commands that scaffold pages from the template package: engine, template and output-path rules.</summary>
internal static class UiScaffold
{
    public static readonly string[] Engines = ["mvc", "razor-pages", "blazor", "fluid"];

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
        if (!Engines.Contains(engine))
            throw new InvalidOperationException(
                engine == "none"
                    ? $"No UI engine found. Pass --engine {string.Join("|", Engines)}, or create the app with --ui-engine."
                    : $"Unknown UI engine '{engine}'. Choose one of: {string.Join(", ", Engines)}.");
        return engine;
    }

    /// <summary>The template file name of a dashboard in an engine. Fluid has its own pages for analytics and audit (the admin starter pages).</summary>
    public static string DashboardTemplateName(UiPageTemplate page, string engine) => (engine, page.Key) switch
    {
        ("fluid", "analytics") => "admin-analytics",
        ("fluid", "audit") => "admin-audit",
        _ => page.TemplateName,
    };

    public static UiPageTemplate FindDashboard(string key) =>
        Dashboards.FirstOrDefault(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException(
            $"Unknown dashboard '{key}'. Choose one of: {string.Join(", ", Dashboards.Select(d => d.Key))}.");

    /// <summary>Path of a template inside the unpacked package, or null when the engine has no such template.</summary>
    public static string? TemplateFile(string templatesDir, string engine, string templateName)
    {
        // Fluid templates are plain Liquid (no Scriban), so they are copied as they are.
        var file = engine == "fluid"
            ? Path.Combine(templatesDir, engine, $"{templateName}.liquid")
            : Path.Combine(templatesDir, engine, $"{templateName}.{(engine == "blazor" ? "razor" : "cshtml")}.sbn");
        return File.Exists(file) ? file : null;
    }

    /// <summary>Where the page goes in the UI project: Views/{Module}, Pages/{Module}, Components/Pages/{Module} or, for Fluid, Templates/{Module} (a page's path is its route).</summary>
    public static string OutputPath(string engine, string uiProjectDir, string module, string pageName) => engine switch
    {
        "mvc" => Path.Combine(uiProjectDir, "Views", module, $"{pageName}.cshtml"),
        "razor-pages" => Path.Combine(uiProjectDir, "Pages", module, $"{pageName}.cshtml"),
        "blazor" => Path.Combine(uiProjectDir, "Components", "Pages", module, $"{pageName}.razor"),
        "fluid" => Path.Combine(uiProjectDir, "Templates", module, $"{pageName}.liquid"),
        _ => throw new ArgumentException($"Unknown UI engine '{engine}'."),
    };
}
