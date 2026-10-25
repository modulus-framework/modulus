namespace Modulus.Storage;

using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Caps the size of a file the app uploads through <see cref="IFileStorage.UploadAsync"/>. A declared length over the limit is refused
/// before any byte is written; a stream of unknown length is refused as soon as it passes the limit. Presigned uploads are not capped
/// here: a presigned PUT (S3, Azure SAS) cannot limit the size it accepts, so an app that needs a cap on those uses a server-side
/// upload or an S3 POST policy.
/// </summary>
internal sealed class SizeLimitedFileStorage(IFileStorage inner, long maxBytes) : IFileStorage
{
    public Task<Stream> DownloadAsync(string path, CancellationToken ct = default) => inner.DownloadAsync(path, ct);

    public Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.CanSeek && content.Length > maxBytes)
            throw Refused(path, maxBytes);

        return inner.UploadAsync(path, new LimitedStream(content, path, maxBytes), contentType, ct);
    }

    public Task DeleteAsync(string path, CancellationToken ct = default) => inner.DeleteAsync(path, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => inner.ExistsAsync(path, ct);

    public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default)
        => inner.GetPresignedUrlAsync(path, expiry, ct);

    public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default)
        => inner.GetPresignedUploadUrlAsync(path, expiry, contentType, ct);

    internal static ValidationException Refused(string path, long maxBytes)
        => new([$"The file '{path}' is larger than the {maxBytes:N0} bytes this app accepts."]);

    /// <summary>Reads through to the inner stream and refuses the read that takes the total past the limit.</summary>
    private sealed class LimitedStream(Stream inner, string path, long maxBytes) : Stream
    {
        private long _total;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => Counted(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Counted(await inner.ReadAsync(buffer, cancellationToken));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Counted(int read)
        {
            _total += read;
            if (_total > maxBytes)
                throw Refused(path, maxBytes);
            return read;
        }
    }
}
