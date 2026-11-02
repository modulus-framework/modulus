namespace Modulus.Media;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Storage;

/// <summary>The media response: the metadata a client may see. The storage key is never part of it.</summary>
/// <param name="Id">The media id.</param>
/// <param name="FileName">The display name.</param>
/// <param name="ContentType">The media type the content is served with.</param>
/// <param name="Kind">Image or Document.</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Sha256">The lowercase hex SHA-256 of the content.</param>
/// <param name="UploadedAt">When it was stored, UTC.</param>
/// <param name="ContentUrl">Where the bytes are served.</param>
public sealed record MediaItemResponse(Guid Id, string FileName, string ContentType, string Kind, long Size, string Sha256, DateTimeOffset UploadedAt, string ContentUrl);

/// <summary>A page of media in the list response.</summary>
public sealed record MediaPageResponse(IReadOnlyList<MediaItemResponse> Items, int Total, int Page, int PageSize);

/// <summary>
/// The media HTTP API. Every route requires its permission (<see cref="MediaPermissions"/>); an anonymous caller gets 401 and a caller
/// without the permission gets 403 from the authorization policy.
/// </summary>
public static class MediaEndpoints
{
    /// <summary>Maps <c>POST</c>, <c>GET</c>, <c>GET {id}</c>, <c>GET {id}/content</c> and <c>DELETE {id}</c> under <paramref name="prefix"/>.</summary>
    public static RouteGroupBuilder MapModulusMedia(this IEndpointRouteBuilder endpoints, string prefix = "/api/media")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix);

        group.MapPost("", (HttpRequest request, IMediaLibrary library, CancellationToken ct) => Answer(async () =>
        {
            // The file is read here, after authorization, not bound as a parameter (see UploadRequest).
            var file = await UploadRequest.RequireFileAsync(request, ct).ConfigureAwait(false);
            await using var content = file.OpenReadStream();
            var item = await library.UploadAsync(content, file.FileName, ct).ConfigureAwait(false);
            return Results.Created($"{prefix}/{item.Id:D}", ToResponse(prefix, item));
        }))
            .RequireAuthorization(MediaPermissions.Manage)
            .DisableAntiforgery();

        group.MapGet("", (string? search, MediaKind? kind, int? page, int? pageSize, IMediaLibrary library, CancellationToken ct) => Answer(async () =>
        {
            var query = new MediaQuery(search, kind, page ?? 1, pageSize ?? 50);
            var result = await library.ListAsync(query, ct).ConfigureAwait(false);
            var items = result.Items.Select(i => ToResponse(prefix, i)).ToList();
            return Results.Ok(new MediaPageResponse(items, result.Total, Math.Max(1, query.Page), Math.Clamp(query.PageSize, 1, MediaQuery.MaxPageSize)));
        }))
            .RequireAuthorization(MediaPermissions.Read);

        group.MapGet("/{id:guid}", (Guid id, IMediaLibrary library, CancellationToken ct) => Answer(async () =>
            Results.Ok(ToResponse(prefix, await library.GetAsync(id, ct).ConfigureAwait(false)))))
            .RequireAuthorization(MediaPermissions.Read);

        group.MapGet("/{id:guid}/content", (Guid id, HttpContext http, IMediaLibrary library, CancellationToken ct) => Answer(async () =>
        {
            var item = await library.GetAsync(id, ct).ConfigureAwait(false);
            var stream = await library.OpenAsync(item, ct).ConfigureAwait(false);

            // The client's type is never trusted; the stored type came from the file's bytes. nosniff stops a browser guessing another type.
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Stream(stream, item.ContentType);
        }))
            .RequireAuthorization(MediaPermissions.Read);

        group.MapDelete("/{id:guid}", (Guid id, IMediaLibrary library, CancellationToken ct) => Answer(async () =>
        {
            await library.DeleteAsync(id, ct).ConfigureAwait(false);
            return Results.NoContent();
        }))
            .RequireAuthorization(MediaPermissions.Manage);

        return group;
    }

    private static MediaItemResponse ToResponse(string prefix, MediaItem item)
        => new(item.Id, item.FileName, item.ContentType, item.Kind.ToString(), item.Size, item.Sha256, item.UploadedAt, $"{prefix}/{item.Id:D}/content");

    private static async Task<IResult> Answer(Func<Task<IResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ex.Errors.ToArray() });
        }
        catch (NotFoundException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (UnauthorizedException)
        {
            return Results.Unauthorized();
        }
    }
}
