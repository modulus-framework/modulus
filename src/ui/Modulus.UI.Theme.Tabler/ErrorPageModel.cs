namespace Modulus.UI.Theming.Tabler;

/// <summary>View model for the shared standalone error document (<c>_ErrorShell</c>).</summary>
/// <param name="Code">HTTP status code.</param>
/// <param name="Title">Short heading.</param>
/// <param name="Text">Explanatory sentence.</param>
public sealed record ErrorPageModel(int Code, string Title, string Text);
