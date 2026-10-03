namespace Modulus.Storage;

using Modulus.Core.Abstractions;

/// <summary>
/// Puts every path of the inner <see cref="IFileStorage"/> under the ambient tenant's own prefix:
/// <c>tenants/{tenantId:N}/</c>, or <c>host/</c> in the host context. Callers keep using relative
/// paths and can never name another tenant's file. With multi-tenancy configured but no tenant
/// resolved, every call throws (fail-closed, like the tenant query filter). Applied to the local,
/// S3 and Azure Blob providers when <see cref="StorageOptions.IsolateTenants"/> is on.
/// </summary>
public sealed class TenantScopedFileStorage(IFileStorage inner, ICurrentTenant currentTenant) : IFileStorage
{
    /// <summary>Prefix of the host context's files.</summary>
    public const string HostPrefix = "host/";

    /// <summary>The storage this decorator forwards to (prefixed paths).</summary>
    public IFileStorage Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
        => Inner.DownloadAsync(Scope(path), ct);

    public Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
        => Inner.UploadAsync(Scope(path), content, contentType, ct);

    public Task DeleteAsync(string path, CancellationToken ct = default)
        => Inner.DeleteAsync(Scope(path), ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Inner.ExistsAsync(Scope(path), ct);

    public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default)
        => Inner.GetPresignedUrlAsync(Scope(path), expiry, ct);

    public Task<string> GetPresignedUploadUrlAsync(
        string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => Inner.GetPresignedUploadUrlAsync(Scope(path), expiry, contentType, ct);

    /// <summary>
    /// The tenant prefix plus <paramref name="path"/>. Rooted paths and <c>.</c>/<c>..</c> segments
    /// are rejected before prefixing: the local provider canonicalizes <c>..</c>, so
    /// <c>../{other}/x</c> would otherwise land in another tenant's folder while still inside the base path.
    /// </summary>
    public string Scope(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(path))
            throw new ArgumentException("Storage paths must be relative.", nameof(path));

        foreach (var segment in normalized.Split('/'))
        {
            if (segment is "." or "..")
                throw new ArgumentException("Storage paths must not contain '.' or '..' segments.", nameof(path));
        }

        return Prefix() + normalized;
    }

    private string Prefix()
    {
        if (currentTenant.TenantId is { } tenantId && !currentTenant.IsHost)
            return $"tenants/{tenantId:N}/";
        if (currentTenant.IsHost)
            return HostPrefix;

        throw new InvalidOperationException(
            "No tenant is in scope: tenant-isolated file storage refuses to read or write. " +
            "Resolve a tenant, or enter the host context with ICurrentTenant.Change(null).");
    }
}
