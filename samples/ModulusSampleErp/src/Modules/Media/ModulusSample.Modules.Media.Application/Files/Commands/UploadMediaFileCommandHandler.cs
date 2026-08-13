namespace ModulusSample.Modules.Media.Application.Files.Commands;

using Modulus.EntityFrameworkCore.Abstractions;

using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Files.Commands;
using ModulusSample.Modules.Media.Application.Files.Dtos;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Domain.Enums;
using ModulusSample.Modules.Media.Domain.Repositories;
using ModulusSample.Modules.Media.Domain.Services;
using ModulusSample.Modules.Media.Domain.ValueObjects;

/// <summary>
/// Uploads a file to the configured object store (MinIO / S3), records the
/// media file, and extracts dimensions for images.
/// </summary>
public sealed class UploadMediaFileCommandHandler
    : ICommandHandler<UploadMediaFileCommand, UploadMediaFileResponse>
{
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IMediaFolderRepository _mediaFolderRepository;
    private readonly IMediaStorageService _storageService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<UploadMediaFileCommandHandler> _logger;

    public UploadMediaFileCommandHandler(
        IMediaFileRepository mediaFileRepository,
        IMediaFolderRepository mediaFolderRepository,
        IMediaStorageService storageService,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        ILogger<UploadMediaFileCommandHandler> logger)
    {
        _mediaFileRepository = mediaFileRepository;
        _mediaFolderRepository = mediaFolderRepository;
        _storageService = storageService;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task<UploadMediaFileResponse> HandleAsync(
        UploadMediaFileCommand command,
        CancellationToken ct)
    {
        try
        {
            var mediaFile = await UploadToStoreAsync(command, ct);
            var response = new UploadMediaFileResponse
            {
                MediaFileId = mediaFile.Id,
                FileName = mediaFile.FileName,
                StoragePath = mediaFile.StoragePath,
                FileUrl = await _storageService.GetPresignedUrlAsync(mediaFile.StoragePath, TimeSpan.FromHours(24), ct),
                FileType = mediaFile.FileType,
                FileSizeBytes = mediaFile.FileSizeBytes
            };

            await ProcessImageMetadataAsync(mediaFile, ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload media file: {FileName}", command.FileName);
            throw;
        }
    }

    private async Task<MediaFile> UploadToStoreAsync(UploadMediaFileCommand command, CancellationToken ct)
    {
        var extension = Path.GetExtension(command.FileName).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = "bin";
        }

        var fileType = DetermineFileType(extension, command.ContentType);
        var storagePath = await GenerateStoragePathAsync(extension, command.FolderId, ct);

        await _storageService.UploadFileAsync(storagePath, command.FileContent, command.ContentType, ct);

        var mediaFile = new MediaFile(
            Guid.NewGuid(),
            command.FileName,
            command.FileName,
            extension,
            command.ContentType,
            command.FileSize,
            storagePath,
            fileType,
            command.FolderId,
            _currentTenant.TenantId,
            _currentUser.UserId);

        if (!string.IsNullOrWhiteSpace(command.AltText) || !string.IsNullOrWhiteSpace(command.Description))
        {
            mediaFile.UpdateMetadata(command.AltText, command.Description);
        }

        await _mediaFileRepository.AddAsync(mediaFile, ct);

        if (command.FolderId.HasValue)
        {
            var folder = await _mediaFolderRepository.GetByIdAsync(command.FolderId.Value, ct);
            if (folder is not null)
            {
                folder.IncrementFileCount();
                await _mediaFolderRepository.UpdateAsync(folder, ct);
            }
        }

        await _unitOfWork.CommitAsync(ct);
        return mediaFile;
    }

    private async Task ProcessImageMetadataAsync(MediaFile mediaFile, CancellationToken ct)
    {
        if (mediaFile.FileType != MediaFileType.Image)
        {
            return;
        }

        var dimensions = await _storageService.GetImageDimensionsAsync(mediaFile.StoragePath, ct);
        if (dimensions is null)
        {
            return;
        }

        mediaFile.MarkAsProcessed(null, dimensions);
        await _mediaFileRepository.UpdateAsync(mediaFile, ct);
        await _unitOfWork.CommitAsync(ct);
    }

    private async Task<string> GenerateStoragePathAsync(string extension, Guid? folderId, CancellationToken ct)
    {
        var segments = new List<string>
        {
            "media",
            DateTime.UtcNow.ToString("yyyy/MM/dd")
        };

        if (folderId.HasValue)
        {
            var folder = await _mediaFolderRepository.GetByIdAsync(folderId.Value, ct);
            if (folder is not null)
            {
                segments.AddRange(folder.Path.Split('/', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        segments.Add($"{Guid.NewGuid():N}.{extension}");
        return string.Join("/", segments);
    }

    private static MediaFileType DetermineFileType(string extension, string contentType)
    {
        var ext = extension.ToLowerInvariant();
        var ct = contentType.ToLowerInvariant();

        if (ct.StartsWith("image/")) return MediaFileType.Image;
        if (ct.StartsWith("video/")) return MediaFileType.Video;
        if (ct.StartsWith("audio/")) return MediaFileType.Audio;
        if (ct.Contains("pdf") || ct.Contains("document") || ct.Contains("text")) return MediaFileType.Document;

        return ext switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp" or "svg" or "tiff" => MediaFileType.Image,
            "mp4" or "avi" or "mov" or "wmv" or "flv" or "webm" or "mkv" => MediaFileType.Video,
            "mp3" or "wav" or "ogg" or "flac" or "aac" => MediaFileType.Audio,
            "pdf" or "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "txt" or "md" => MediaFileType.Document,
            "zip" or "rar" or "7z" or "tar" or "gz" => MediaFileType.Archive,
            _ => MediaFileType.Other
        };
    }
}
