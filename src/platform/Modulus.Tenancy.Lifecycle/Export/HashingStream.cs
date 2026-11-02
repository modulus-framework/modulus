namespace Modulus.Tenancy.Lifecycle.Export;

using System.Security.Cryptography;

/// <summary>A write-only stream that counts and hashes what passes through to an inner stream.</summary>
internal sealed class HashingStream(Stream inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    /// <summary>The bytes written so far.</summary>
    public long Bytes { get; private set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => Bytes;
        set => throw new NotSupportedException();
    }

    /// <summary>The lower-case hex SHA-256 of everything written.</summary>
    public string Sha256Hex() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        _hash.AppendData(buffer, offset, count);
        Bytes += count;
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _hash.AppendData(buffer);
        Bytes += buffer.Length;
        inner.Write(buffer);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _hash.AppendData(buffer, offset, count);
        Bytes += count;
        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _hash.AppendData(buffer.Span);
        Bytes += buffer.Length;
        await inner.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
            inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
