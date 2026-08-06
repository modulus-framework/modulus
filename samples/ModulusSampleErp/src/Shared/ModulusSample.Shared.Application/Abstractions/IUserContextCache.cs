namespace ModulusSample.Shared.Application.Abstractions;

/// <summary>
/// Generic user context cache interface to avoid circular dependencies.
/// The actual implementation is in the Users module.
/// </summary>
public interface IUserContextCacheService
{
    /// <summary>
    /// Gets a user by external identity provider ID with caching.
    /// </summary>
    Task<object?> GetUserByExternalIdAsync(string externalId, CancellationToken ct);
}
