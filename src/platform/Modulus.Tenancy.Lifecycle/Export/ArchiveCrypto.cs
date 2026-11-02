namespace Modulus.Tenancy.Lifecycle.Export;

using System.Buffers.Binary;
using System.Security.Cryptography;

/// <summary>
/// Chunked AES-256-GCM for export archives. The file is a header (magic and a random 4-byte nonce prefix), then chunks of up to
/// 64 KiB, each with its length, a final flag, and its ciphertext and tag. Every chunk's nonce is the prefix and a counter, and the
/// counter and the final flag are authenticated, so a changed, reordered, dropped or truncated chunk fails the check.
/// </summary>
internal static class ArchiveCrypto
{
    /// <summary>The header's magic and format version.</summary>
    public static readonly byte[] Magic = "MODLX1"u8.ToArray();

    public const int KeyBytes = 32;
    private const int ChunkSize = 64 * 1024;
    private const int TagSize = 16;
    private const int PrefixSize = 4;

    /// <summary>Checks the key length; an archive key must be 32 bytes.</summary>
    public static byte[] ParseKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException("TenantLifecycle:ExportEncryptionKey is not set: exports cannot be encrypted.");
        var key = Convert.FromBase64String(base64);
        if (key.Length != KeyBytes)
            throw new InvalidOperationException("TenantLifecycle:ExportEncryptionKey must decode to 32 bytes.");
        return key;
    }

    /// <summary>Encrypts <paramref name="plaintext"/> to <paramref name="destination"/>.</summary>
    public static async Task EncryptAsync(Stream plaintext, Stream destination, byte[] key, CancellationToken ct)
    {
        using var aes = new AesGcm(key, TagSize);
        var prefix = RandomNumberGenerator.GetBytes(PrefixSize);
        await destination.WriteAsync(Magic, ct);
        await destination.WriteAsync(prefix, ct);

        var buffer = new byte[ChunkSize];
        var ciphertext = new byte[ChunkSize];
        var tag = new byte[TagSize];
        var header = new byte[5];
        ulong counter = 0;
        while (true)
        {
            var read = await ReadChunkAsync(plaintext, buffer, ct);
            var final = read < ChunkSize;
            aes.Encrypt(Nonce(prefix, counter), buffer.AsSpan(0, read), ciphertext.AsSpan(0, read), tag, Aad(counter, final));

            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)read);
            header[4] = final ? (byte)1 : (byte)0;
            await destination.WriteAsync(header, ct);
            await destination.WriteAsync(ciphertext.AsMemory(0, read), ct);
            await destination.WriteAsync(tag, ct);

            if (final)
                return;
            counter++;
        }
    }

    /// <summary>
    /// Decrypts <paramref name="source"/>. Each chunk is authenticated before its plaintext is written to
    /// <paramref name="destination"/>, so a damaged file can release a prefix of its plaintext. To release nothing before the whole
    /// file is verified, run once with <see langword="null"/> (verify only) and then again with the destination.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not an archive, is damaged, truncated, or was encrypted with another key.</exception>
    public static async Task DecryptAsync(Stream source, Stream? destination, byte[] key, CancellationToken ct)
    {
        using var aes = new AesGcm(key, TagSize);
        var magic = new byte[Magic.Length];
        var prefix = new byte[PrefixSize];
        await ReadExactlyAsync(source, magic, ct);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("The file is not an export archive.");
        await ReadExactlyAsync(source, prefix, ct);

        var header = new byte[5];
        var ciphertext = new byte[ChunkSize];
        var tag = new byte[TagSize];
        var plain = new byte[ChunkSize];
        ulong counter = 0;
        while (true)
        {
            await ReadExactlyAsync(source, header, ct);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length > ChunkSize || header[4] > 1)
                throw new InvalidDataException("The export archive is damaged.");
            var final = header[4] == 1;

            await ReadExactlyAsync(source, ciphertext.AsMemory(0, length), ct);
            await ReadExactlyAsync(source, tag, ct);
            try
            {
                aes.Decrypt(Nonce(prefix, counter), ciphertext.AsSpan(0, length), tag, plain.AsSpan(0, length), Aad(counter, final));
            }
            catch (CryptographicException)
            {
                throw new InvalidDataException("The export archive failed authentication: it is damaged or was encrypted with another key.");
            }

            if (destination is not null)
                await destination.WriteAsync(plain.AsMemory(0, length), ct);

            if (final)
            {
                if (source.ReadByte() != -1)
                    throw new InvalidDataException("The export archive has data after its last chunk.");
                return;
            }
            counter++;
        }
    }

    private static byte[] Nonce(byte[] prefix, ulong counter)
    {
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(PrefixSize), counter);
        return nonce;
    }

    private static byte[] Aad(ulong counter, bool final)
    {
        var aad = new byte[9];
        BinaryPrimitives.WriteUInt64BigEndian(aad, counter);
        aad[8] = final ? (byte)1 : (byte)0;
        return aad;
    }

    private static async Task<int> ReadChunkAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], ct);
            if (read == 0)
                throw new InvalidDataException("The export archive is truncated.");
            total += read;
        }
    }
}
