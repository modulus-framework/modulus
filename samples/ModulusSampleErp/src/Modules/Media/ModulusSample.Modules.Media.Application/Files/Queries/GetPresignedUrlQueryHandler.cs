namespace ModulusSample.Modules.Media.Application.Files.Queries;

using Modulus.EntityFrameworkCore.Abstractions;

using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Files.Dtos;
using ModulusSample.Modules.Media.Application.Files.Queries;
using ModulusSample.Modules.Media.Domain.Repositories;
using ModulusSample.Modules.Media.Domain.Services;

/// <summary>
/// Generates a temporary presigned URL for an object in the store.
/// </summary>
public sealed class GetPresignedUrlQueryHandler
    : IQueryHandler<GetPresignedUrlQuery, PresignedUrlResponse>
{
    private readonly IMediaStorageService _storageService;
    private readonly ILogger<GetPresignedUrlQueryHandler> _logger;

    public GetPresignedUrlQueryHandler(
        IMediaStorageService storageService,
        ILogger<GetPresignedUrlQueryHandler> logger)
    {
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<PresignedUrlResponse> HandleAsync(
        GetPresignedUrlQuery query,
        CancellationToken ct)
    {
        try
        {
            var expiration = query.Expiration ?? TimeSpan.FromHours(1);
            var url = await _storageService.GetPresignedUrlAsync(query.StoragePath, expiration, ct);

            return new PresignedUrlResponse
            {
                Url = url,
                ExpiresAt = DateTime.UtcNow.Add(expiration)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate presigned URL for: {StoragePath}", query.StoragePath);
            throw;
        }
    }
}
