namespace Modulus.AspNetCore.DataProtection;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.DataProtection;

/// <summary>
/// <see cref="IPersonalDataProtector"/> backed by ASP.NET Core Data Protection. Data
/// Protection owns key storage, ring management, and rotation, so ciphertext produced
/// under an older key keeps decrypting after rotation without re-encrypting rows.
/// Deterministic search hashes use HMAC-SHA256 with a separately configured key.
/// </summary>
internal sealed class DataProtectionPersonalDataProtector : IPersonalDataProtector
{
    private readonly IDataProtector _protector;
    private readonly byte[]? _hashKey;

    public DataProtectionPersonalDataProtector(
        IDataProtectionProvider provider,
        IOptions<PersonalDataProtectionOptions> options)
    {
        var settings = options.Value;
        _protector = provider.CreateProtector(settings.Purpose);
        _hashKey = string.IsNullOrWhiteSpace(settings.SearchHashKey)
            ? null
            : Convert.FromBase64String(settings.SearchHashKey);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);

    public string Hash(string value)
    {
        if (_hashKey is null)
            throw new InvalidOperationException(
                $"{PersonalDataProtectionOptions.SectionName}:{nameof(PersonalDataProtectionOptions.SearchHashKey)} " +
                "is not configured. A base64-encoded HMAC-SHA256 key is required to compute " +
                "deterministic search hashes for encrypted personal data.");

        var mac = HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(mac);
    }
}
