using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Commands;
using ModulusSample.Modules.Media.Application.Dtos;
using ModulusSample.Modules.Media.Application.Queries;

namespace ModulusSample.Modules.Media.Presentation.Controllers;

[ApiController]
[Route("api/media/folders")]
[Authorize]
public sealed class FoldersController : ControllerBase
{
    private readonly IMediator _mediator;

    public FoldersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Create a new media folder.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(MediaFolderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MediaFolderDto>> Create(
        [FromBody] CreateMediaFolderRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.SendAsync(
            new CreateMediaFolderCommand(request.Name, request.Description, request.ParentFolderId), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Get a media folder by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MediaFolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MediaFolderDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.QueryAsync(new GetMediaFolderByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Get media folders, optionally scoped to a parent folder.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MediaFolderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MediaFolderDto>>> GetByParent(
        [FromQuery] Guid? parentFolderId = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.QueryAsync<IReadOnlyList<MediaFolderDto>>(new GetMediaFoldersQuery(parentFolderId), ct);
        return Ok(result);
    }

    /// <summary>
    /// Rename a media folder.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateMediaFolderRequest request,
        CancellationToken ct = default)
    {
        await _mediator.SendAsync(new UpdateMediaFolderCommand(id, request.Name, request.Description), ct);
        return NoContent();
    }

    /// <summary>
    /// Delete an empty media folder.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.SendAsync(new DeleteMediaFolderCommand(id), ct);
        return NoContent();
    }
}

public sealed record CreateMediaFolderRequest(string Name, string? Description = null, Guid? ParentFolderId = null);
public sealed record UpdateMediaFolderRequest(string Name, string? Description = null);
