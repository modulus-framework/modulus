namespace Modulus.Cli.Services;

/// <summary>A reusable UI component the <c>Modulus.Ui.Templates</c> package ships for every engine.</summary>
internal sealed record UiComponent(string Key, string Category, string Description)
{
    /// <summary>"form-group" → "FormGroup".</summary>
    public string PascalName => string.Concat(Key.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}

internal static class UiComponents
{
    public static readonly IReadOnlyList<UiComponent> All =
    [
        new("alert", "Feedback", "Info, success, warning and danger messages; can be dismissed."),
        new("avatar", "Data display", "A person's image, or their initials."),
        new("badge", "Data display", "A small status label."),
        new("breadcrumb", "Navigation", "The path to the current page."),
        new("button", "Inputs", "Button with variants and sizes."),
        new("card", "Layout", "A container with header, body and footer."),
        new("data-table", "Data display", "A table with columns and an empty message."),
        new("dropdown", "Navigation", "A button that opens a menu of links."),
        new("empty-state", "Feedback", "Shown when a list or search has no results."),
        new("form-group", "Inputs", "Label, hint and error around any control."),
        new("form-input", "Inputs", "A labelled text field with hint and validation."),
        new("modal", "Feedback", "A dialog over the page."),
        new("pagination", "Navigation", "Page links for a list."),
        new("progress", "Feedback", "A progress bar."),
        new("spinner", "Feedback", "A loading indicator."),
        new("stat-card", "Data display", "One headline number with a label and change."),
        new("tabs", "Navigation", "Switch between panels."),
    ];

    public static UiComponent Find(string key) =>
        All.FirstOrDefault(c => c.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown component '{key}'. Run 'modulus ui add-component --list' to see them.");

    /// <summary>Where the component goes: a partial under Views/Shared or Pages/Shared, or a Razor component under Components/Shared.</summary>
    public static string OutputPath(string engine, string uiProjectDir, UiComponent component) => engine switch
    {
        "mvc" => Path.Combine(uiProjectDir, "Views", "Shared", "Components", $"_{component.PascalName}.cshtml"),
        "razor-pages" => Path.Combine(uiProjectDir, "Pages", "Shared", "Components", $"_{component.PascalName}.cshtml"),
        "blazor" => Path.Combine(uiProjectDir, "Components", "Shared", $"{component.PascalName}.razor"),
        _ => throw new ArgumentException($"Unknown UI engine '{engine}'."),
    };
}
