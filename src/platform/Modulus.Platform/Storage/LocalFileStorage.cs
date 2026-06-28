namespace Modulus.Storage;

using Microsoft.Extensions.Options;

public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _basePath =
        Path.GetFullPath(options.Value.BasePath ?? "storage");

    /// <summary>
    /// Resolves <paramref name="path"/> against <see cref="_basePath"/> and
    /// rejects anything that escapes it (relative traversal like
    /// <c>..\..\secret</c> or absolute paths like <c>/etc/passwd</c> /
    /// <c>C:\Windows\...</c>). Throws <see cref="ArgumentException"/> on
    /// rejection so callers cannot read/overwrite/delete arbitrary files.
    /// </summary>
    private string FullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        var root = _basePath.EndsWith(Path.DirectorySeparatorChar)
            ? _basePath
            : _basePath + Path.DirectorySeparatorChar;

        // Combine, then canonicalize. Path.GetFullPath collapses ".." and
        // resolves absolute inputs verbatim — which we then reject unless
        // they live under the configured base directory.
        var combined = Path.IsPathRooted(path) ? path : Path.Combine(_basePath, path);
        var full = Path.GetFullPath(combined);

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Path '{path}' escapes the storage root '{_basePath}'.", nameof(path));

        return full;
    }

    public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var stream = File.OpenRead(FullPath(path));
        return Task.FromResult<Stream>(stream);
    }

    public async Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        var full = FullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)
            ?? throw new InvalidOperationException("Resolved path has no directory."));
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var full = FullPath(path);
        if (File.Exists(full))
            File.Delete(full);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(FullPath(path)));
    }

    public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult($"/storage/{path}");
}
