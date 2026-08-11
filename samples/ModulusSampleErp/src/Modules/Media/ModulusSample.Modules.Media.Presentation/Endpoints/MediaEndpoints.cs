using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Commands;
using ModulusSample.Modules.Media.Application.Queries;

namespace ModulusSample.Modules.Media.Presentation.Endpoints;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        MapFolderEndpoints(app);
        MapFileEndpoints(app);
    }

    private static void MapFolderEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/media/folders")
            .WithName("MediaFolders")
            .WithDescription("Manage media folder structure")
            .WithOpenApi();

        group.MapPost("/", CreateFolder)
            .WithName("CreateMediaFolder")
            .WithDescription("Create a new media folder")
            .RequireAuthorization();

        group.MapGet("/{id}", GetFolderById)
            .WithName("GetMediaFolderById")
            .WithDescription("Get folder details");

        group.MapGet("/", ListFolders)
            .WithName("ListMediaFolders")
            .WithDescription("List all media folders");

        group.MapPut("/{id}", UpdateFolder)
            .WithName("UpdateMediaFolder")
            .WithDescription("Update folder name or description")
            .RequireAuthorization();

        group.MapDelete("/{id}", DeleteFolder)
            .WithName("DeleteMediaFolder")
            .WithDescription("Delete a media folder")
            .RequireAuthorization();

        group.MapGet("/{id}/contents", GetFolderContents)
            .WithName("GetFolderContents")
            .WithDescription("Get folder contents (subfolders and files)");
    }

    private static void MapFileEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/media/files")
            .WithName("MediaFiles")
            .WithDescription("Manage media files")
            .WithOpenApi();

        group.MapPost("/", UploadFile)
            .WithName("UploadMediaFile")
            .WithDescription("Upload a media file")
            .DisableAntiforgery()
            .RequireAuthorization();

        group.MapGet("/{id}", GetFileById)
            .WithName("GetMediaFileById")
            .WithDescription("Get file details");

        group.MapDelete("/{id}", DeleteFile)
            .WithName("DeleteMediaFile")
            .WithDescription("Delete a media file")
            .RequireAuthorization();

        group.MapGet("/{id}/download", DownloadFile)
            .WithName("DownloadMediaFile")
            .WithDescription("Download a media file");
    }

    // Folder Endpoints
    private static async Task<IResult> CreateFolder(
        CreateMediaFolderRequest request,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync(
            new CreateMediaFolderCommand(request.Name, request.Description, request.ParentFolderId), ct);

        return result.IsSuccess
            ? Results.Created($"/api/media/folders/{result.Value.Id}", result.Value)
            : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> GetFolderById(Guid id, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.QueryAsync(new GetMediaFolderByIdQuery(id), ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> ListFolders(IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.QueryAsync(new ListMediaFoldersQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateFolder(
        Guid id,
        UpdateMediaFolderRequest request,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.SendAsync(
            new UpdateMediaFolderCommand(id, request.Name, request.Description), ct);

        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> DeleteFolder(Guid id, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.SendAsync(new DeleteMediaFolderCommand(id), ct);
        return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> GetFolderContents(Guid id, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.QueryAsync(new GetFolderContentsQuery(id), ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    // File Endpoints
    private static async Task<IResult> UploadFile(
        IFormFile file,
        [FromForm] Guid folderId,
        IMediator mediator,
        CancellationToken ct)
    {
        if (file.Length == 0)
            return Results.BadRequest("File is empty");

        using var stream = file.OpenReadStream();
        var result = await mediator.SendAsync(
            new UploadMediaFileCommand(folderId, file.FileName, file.ContentType, stream), ct);

        return result.IsSuccess
            ? Results.Created($"/api/media/files/{result.Value}", new { id = result.Value })
            : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> GetFileById(Guid id, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.QueryAsync(new GetMediaFileByIdQuery(id), ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> DeleteFile(Guid id, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.SendAsync(new DeleteMediaFileCommand(id), ct);
        return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> DownloadFile(Guid id, IMediator mediator, CancellationToken ct)
    {
        var result = await mediator.QueryAsync(new DownloadMediaFileQuery(id), ct);
        return result is not null
            ? Results.File(result.Stream, result.ContentType, result.FileName)
            : Results.NotFound();
    }
}

// Request DTOs
public sealed record CreateMediaFolderRequest(string Name, string Description, Guid? ParentFolderId = null);
public sealed record UpdateMediaFolderRequest(string Name, string Description);
