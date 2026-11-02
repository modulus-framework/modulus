namespace Modulus.Storage;

using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Reads the single file of an upload from the request body, inside the endpoint, after authorization has run.
/// An upload endpoint must not bind <see cref="IFormFile"/> as a parameter: the binder reads the body before the authorization policy is
/// evaluated, so a caller without the permission could get a binding error (415) instead of a refusal (403).
/// </summary>
public static class UploadRequest
{
    /// <summary>The form field a client sends the file in.</summary>
    public const string FileField = "file";

    /// <summary>Returns the uploaded file. Throws <see cref="ValidationException"/> when the body is not a multipart form with one file.</summary>
    public static async Task<IFormFile> RequireFileAsync(HttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasFormContentType)
            throw new ValidationException([$"The request must be multipart/form-data with a '{FileField}' part."]);

        var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
        var file = form.Files[FileField] ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
            throw new ValidationException([$"The request needs a non-empty '{FileField}' part."]);
        return file;
    }
}
