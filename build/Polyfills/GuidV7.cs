namespace Modulus;

/// <summary><c>Guid.CreateVersion7()</c> exists from .NET 9; net8.0 builds use a time-ordered equivalent (RFC 9562 layout).</summary>
internal static class GuidV7
{
    public static Guid Create() => Create(DateTimeOffset.UtcNow);

    public static Guid Create(DateTimeOffset timestamp)
    {
#if NET9_0_OR_GREATER
        return Guid.CreateVersion7(timestamp);
#else
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var ms = timestamp.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(ms >> 40);
        bytes[1] = (byte)(ms >> 32);
        bytes[2] = (byte)(ms >> 24);
        bytes[3] = (byte)(ms >> 16);
        bytes[4] = (byte)(ms >> 8);
        bytes[5] = (byte)ms;
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
#endif
    }
}
