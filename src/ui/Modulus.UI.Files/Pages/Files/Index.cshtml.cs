namespace Modulus.UI.Files.Pages.Files;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Modulus.Localization;
using Modulus.Storage;
using Modulus.UI;

/// <summary>
/// Path-addressed file manager (<c>/files</c>) over the registered
/// <see cref="IFileStorage"/>: look up existence, upload
/// (<c>multipart/form-data</c>), download, and delete by explicit path.
/// Rejected paths (traversal / absolute escapes) surface as validation
/// errors; uploads are capped by <c>FilesUi:MaxUploadBytes</c>.
/// </summary>
/// <remarks>
/// HTMX behavior: upload and delete re-render the result card
/// (<c>_FileResult</c> partial) in place with a toast; validation failures
/// re-render the card with the error summary. Non-JS callers keep the classic
/// redirect/page flow. Lookup and download stay full-page (bookmarkable URL
/// and real file download respectively).
/// </remarks>
public sealed class IndexModel(
    IFileStorage storage,
    IOptions<FilesUiOptions> options,
    IModulusLocalizer localizer) : HtmxPageModel
{
    private const string ContentTypeFallback = "application/octet-stream";

    private readonly IFileStorage _storage = storage;
    private readonly IOptions<FilesUiOptions> _options = options;
    private readonly IModulusLocalizer _localizer = localizer;

    [BindProperty(SupportsGet = true)]
    public string? Path { get; set; }

    public bool? Exists { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        try
        {
            Exists = await _storage.ExistsAsync(Path, ct);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(Path), await TextAsync("Index.InvalidPath"));
        }
    }

    public async Task<IActionResult> OnGetDownloadAsync(string path, CancellationToken ct)
    {
        Stream content;
        try
        {
            content = await _storage.DownloadAsync(path, ct);
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound();
        }

        return File(content, ContentTypeFallback, System.IO.Path.GetFileName(path));
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Path))
            ModelState.AddModelError(nameof(Path), await TextAsync("Index.PathRequired"));
        if (file is null || file.Length == 0)
            ModelState.AddModelError("file", await TextAsync("Index.FileRequired"));
        else if (file.Length > _options.Value.MaxUploadBytes)
            ModelState.AddModelError("file", await TextAsync("Index.TooLarge"));

        if (!ModelState.IsValid)
        {
            await OnGetAsync(ct);
            return IsHtmxRequest
                ? HtmxPartial("_FileResult", ResultView())
                : Page();
        }

        try
        {
            await using var stream = file!.OpenReadStream();
            await _storage.UploadAsync(Path!, stream, file.ContentType, ct);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(Path), await TextAsync("Index.InvalidPath"));
            await OnGetAsync(ct);
            return IsHtmxRequest
                ? HtmxPartial("_FileResult", ResultView())
                : Page();
        }

        await OnGetAsync(ct);

        if (!IsHtmxRequest)
            return RedirectToPage(new { Path });

        HtmxToast(await TextAsync("Index.Uploaded"));
        return HtmxPartial("_FileResult", ResultView());
    }

    public async Task<IActionResult> OnPostDeleteAsync(string path, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteAsync(path, ct);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(nameof(Path), await TextAsync("Index.InvalidPath"));
            Path = path;
            await OnGetAsync(ct);
            return IsHtmxRequest
                ? HtmxPartial("_FileResult", ResultView())
                : Page();
        }

        if (!IsHtmxRequest)
            return RedirectToPage(new { Path = path });

        Path = path;
        Exists = false;
        HtmxToast(await TextAsync("Index.Deleted"));
        return HtmxPartial("_FileResult", ResultView());
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(FilesUiLocalization.ResourceName, key);

    /// <summary>Builds the result-card fragment model from current state.</summary>
    public FileResultView ResultView() => new(Path ?? string.Empty, Exists);
}

/// <summary>
/// Fragment model for the file result card (<c>_FileResult</c> partial).
/// </summary>
/// <param name="Path">The looked-up storage path.</param>
/// <param name="Exists">
/// Lookup outcome; <c>null</c> when the card only carries validation errors.
/// </param>
public sealed record FileResultView(string Path, bool? Exists);
