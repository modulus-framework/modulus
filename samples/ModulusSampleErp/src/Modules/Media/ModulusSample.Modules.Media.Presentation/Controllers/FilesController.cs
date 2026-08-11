using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Commands;
using ModulusSample.Modules.Media.Application.Dtos;
using ModulusSample.Modules.Media.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Media.Presentation.Controllers;

[ApiController]
[Route("api/media/files")]
[Authorize]
public sealed class FilesController : ControllerBase
{
    private const long MaxUploadBytes = 100L * 1024 * 1024; // 100 MB

    private readonly IMediator _mediator;

    public FilesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Upload a media file. The file is streamed to the configured object
    /// store (MinIO / S3) and its metadata recorded in the database.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(typeof(UploadMediaFileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UploadMediaFileResponse>> Upload(
        IFormFile file,
        [FromForm] Guid? folderId = null,
        [FromForm] string? altText = null,
        [FromForm] string? description = null,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("A non-empty file is required.");
        }

        if (file.Length > MaxUploadBytes)
        {
            return BadRequest($"File exceeds the maximum allowed size of {MaxUploadBytes / (1024 * 1024)} MB.");
        }

        await using var stream = file.OpenReadStream();
        var command = new UploadMediaFileCommand(
            file.FileName,
            file.ContentType,
            file.Length,
            stream,
            folderId,
            altText,
            description);

        var result = await _mediator.SendAsync(command, ct);
        return Ok(result);
    }

    /// <summary>
    /// Get a media file by ID with short-lived presigned URLs.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MediaFileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaFileDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.QueryAsync(new GetMediaFileByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Get paged media files, optionally scoped to a folder.
    /// </summary>
    [HttpGet("by-folder")]
    [ProducesResponseType(typeof(PagedResult<MediaFileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MediaFileDto>>> GetByFolder(
        [FromQuery] Guid? folderId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.QueryAsync(new GetMediaFilesByFolderQuery(folderId, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>
    /// Search media files by name, alt text, or description.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<MediaFileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MediaFileDto>>> Search(
        [FromQuery] string searchTerm,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.QueryAsync(new SearchMediaFilesQuery(searchTerm, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>
    /// Generate a presigned URL for an existing media file.
    /// </summary>
    [HttpGet("{id:guid}/presigned-url")]
    [ProducesResponseType(typeof(PresignedUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PresignedUrlResponse>> GetPresignedUrl(
        Guid id,
        [FromQuery] int expirationHours = 1,
        CancellationToken ct = default)
    {
        var file = await _mediator.QueryAsync(new GetMediaFileByIdQuery(id), ct);
        if (file is null)
        {
            return NotFound();
        }

        var result = await _mediator.QueryAsync(
            new GetPresignedUrlQuery(file.StoragePath, TimeSpan.FromHours(expirationHours)), ct);
        return Ok(result);
    }

    /// <summary>
    /// Update media file metadata (alt text / description).
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMetadata(
        Guid id,
        [FromBody] UpdateMediaFileMetadataRequest request,
        CancellationToken ct = default)
    {
        await _mediator.SendAsync(new UpdateMediaFileMetadataCommand(id, request.AltText, request.Description), ct);
        return NoContent();
    }

    /// <summary>
    /// Delete a media file from the store and the database.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.SendAsync(new DeleteMediaFileCommand(id), ct);
        return NoContent();
    }
}

public sealed record UpdateMediaFileMetadataRequest(string? AltText = null, string? Description = null);
