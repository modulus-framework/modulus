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
        new("auto-complete", "Inputs", "A text field that suggests values while you type."),
        new("button", "Inputs", "Button with variants and sizes."),
        new("cascader", "Inputs", "Pick a value down a hierarchy, such as country, state, city."),
        new("checkbox", "Inputs", "A single tick box with a label."),
        new("checkbox-group", "Inputs", "Several choices from a short list."),
        new("color-picker", "Inputs", "Pick a colour from a palette or enter a hex value."),
        new("date-picker", "Inputs", "A calendar pop-up for a date, month, year or time."),
        new("date-range-picker", "Inputs", "Pick a start and an end date."),
        new("form-group", "Inputs", "Label, hint and error around any control."),
        new("form-input", "Inputs", "A labelled text field with hint and validation."),
        new("input-number", "Inputs", "A number field with stepper buttons and limits."),
        new("money-input", "Inputs", "An amount with a currency picker."),
        new("otp-input", "Inputs", "A row of boxes for a sign-in or verification code."),
        new("radio-group", "Inputs", "One choice from a few, as radios or button tabs."),
        new("rate", "Inputs", "A star rating."),
        new("search-input", "Inputs", "A search field with a search button."),
        new("segmented", "Inputs", "A compact switch between a few options."),
        new("select", "Inputs", "A drop-down list of choices, single or multiple."),
        new("slider", "Inputs", "Pick a number by dragging a handle."),
        new("switch", "Inputs", "An on/off toggle."),
        new("tag-input", "Inputs", "Type values and press Enter to make tags; posts one value per tag."),
        new("textarea", "Inputs", "A multi-line text field with an optional character count."),
        new("time-picker", "Inputs", "A time of day with hour, minute and second columns."),
        new("transfer", "Inputs", "Move items between two lists."),
        new("tree-select", "Inputs", "A drop-down tree for picking one or many nodes."),
        new("upload", "Inputs", "A file picker with a drop zone and a file list."),
        new("avatar", "Data display", "A person's image, or their initials."),
        new("badge", "Data display", "A small status label."),
        new("calendar", "Data display", "A month or year calendar to show or pick days."),
        new("carousel", "Data display", "A slideshow of panels that can play by itself."),
        new("collapse", "Data display", "Panels that open and close; one at a time as an accordion."),
        new("countdown", "Data display", "A countdown to a date and time."),
        new("data-table", "Data display", "A table with columns and an empty message."),
        new("descriptions", "Data display", "A read-only list of label and value pairs, for a record or a profile."),
        new("image", "Data display", "An image with a click-to-zoom preview."),
        new("list", "Data display", "A vertical list of rows with an avatar, title and description."),
        new("popover", "Data display", "A small card with a title and text that opens on click or hover."),
        new("qr-code", "Data display", "A QR code for a link or text."),
        new("stat-card", "Data display", "One headline number with a label and change."),
        new("statistic", "Data display", "A number with a title, prefix and suffix."),
        new("status-badge", "Data display", "A pill for a status such as active, pending or failed."),
        new("tag", "Data display", "A coloured label for a category, a status or a keyword."),
        new("timeline", "Data display", "Events in time order, with a coloured dot for each."),
        new("tooltip", "Data display", "A short hint that appears when you hover or focus an element."),
        new("tree", "Data display", "A hierarchy of expandable nodes, with optional checkboxes."),
        new("back-top", "Navigation", "A button that scrolls back to the top of a long page."),
        new("breadcrumb", "Navigation", "The path to the current page."),
        new("dropdown", "Navigation", "A button that opens a menu of links."),
        new("menu", "Navigation", "A vertical, inline or horizontal menu with nested groups."),
        new("page-header", "Navigation", "The title block at the top of a page, with a sub-title and actions."),
        new("pagination", "Navigation", "Page links for a list."),
        new("steps", "Navigation", "Show the stages of a process and the current one."),
        new("tabs", "Navigation", "Switch between panels."),
        new("card", "Layout", "A container with header, body and footer."),
        new("divider", "Layout", "A line that separates content, with an optional label."),
        new("grid", "Layout", "Rows and columns on a 24-column grid that stacks on phones."),
        new("space", "Layout", "Even spacing between a row or a stack of elements."),
        new("splitter", "Layout", "Two or more panels with a draggable divider between them."),
        new("alert", "Feedback", "Info, success, warning and danger messages; can be dismissed."),
        new("drawer", "Feedback", "A panel that slides in from the edge of the page."),
        new("empty-state", "Feedback", "Shown when a list or search has no results."),
        new("modal", "Feedback", "A dialog over the page."),
        new("popconfirm", "Feedback", "An inline OK / Cancel question before an action, such as delete."),
        new("progress", "Feedback", "A progress bar."),
        new("result", "Feedback", "A full result page for success, an error or a missing page."),
        new("skeleton", "Feedback", "A grey placeholder shown while content loads."),
        new("spinner", "Feedback", "A loading indicator."),
        new("document", "Documents", "A Word-style document editor: headings, lists, tables and images; saves as HTML."),
        new("pdf-viewer", "Documents", "View a PDF in the page, with page controls and zoom."),
        new("spreadsheet", "Documents", "An Excel-style spreadsheet: formulas, several sheets, formatting."),
        new("code-editor", "Editors", "A code box with syntax colours for SQL, JSON, C#, HTML and more."),
        new("rich-text-editor", "Editors", "A rich text box: bold, lists, links and images; saves as HTML."),
        new("data-grid", "Data grids", "An editable, Excel-like data grid: edit cells, search, filter, sort, page, export and batch actions."),
        new("import-wizard", "Data grids", "Import an Excel or CSV file in steps: upload, match columns, check, import."),
        new("pivot-grid", "Data grids", "A pivot table: rows, columns and totals from your data, with CSV download."),
        new("file-manager", "Pro", "Browse folders and files; create, rename and delete."),
        new("gantt", "Pro", "A project timeline with bars, dependencies and a today line."),
        new("kanban", "Pro", "A drag-and-drop board with columns and cards."),
        new("query-builder", "Pro", "Build a filter with fields, operators and nested AND/OR groups."),
        new("scheduler", "Pro", "A day, week and month calendar of events with categories."),
        new("schema-form", "Pro", "A whole form drawn from a schema of fields."),
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
