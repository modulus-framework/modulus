namespace Modulus.UI;

/// <summary>
/// Model for the shared <c>_Alert</c> partial: a dismissible Tabler alert with
/// Alpine auto-hide. <see cref="Type"/> is a Tabler alert color
/// (success, info, warning, danger).
/// </summary>
/// <param name="Type">Tabler alert color.</param>
/// <param name="Message">Alert text (rendered as plain text, never HTML).</param>
public sealed record UiAlert(string Type, string Message)
{
    /// <summary>Creates a success alert.</summary>
    public static UiAlert Success(string message) => new("success", message);

    /// <summary>Creates an informational alert.</summary>
    public static UiAlert Info(string message) => new("info", message);

    /// <summary>Creates a warning alert.</summary>
    public static UiAlert Warning(string message) => new("warning", message);

    /// <summary>Creates a danger alert.</summary>
    public static UiAlert Danger(string message) => new("danger", message);
}
