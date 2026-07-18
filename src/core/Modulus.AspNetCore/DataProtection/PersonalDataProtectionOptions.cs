namespace Modulus.AspNetCore.DataProtection;

/// <summary>
/// Options for transparent personal-data encryption, bound from the
/// <c>PersonalDataProtection</c> configuration section.
/// </summary>
public sealed class PersonalDataProtectionOptions
{
    /// <summary>Configuration section name (<c>PersonalDataProtection</c>).</summary>
    public const string SectionName = "PersonalDataProtection";

    /// <summary>
    /// When <c>false</c>, no <see cref="Modulus.Core.Abstractions.DataProtection.IPersonalDataProtector"/>
    /// is registered, so <see cref="Modulus.Core.Abstractions.DataProtection.ProtectedPersonalDataAttribute"/>
    /// columns are stored as plaintext. Defaults to <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The Data Protection purpose string that scopes the encryption keys. Keep it
    /// <b>stable</b> for the lifetime of the data: changing it makes existing
    /// ciphertext undecryptable. Defaults to <c>Modulus.PersonalData.Protector.v1</c>.
    /// </summary>
    public string Purpose { get; set; } = "Modulus.PersonalData.Protector.v1";

    /// <summary>
    /// Base64-encoded HMAC-SHA256 key used by
    /// <see cref="Modulus.Core.Abstractions.DataProtection.IPersonalDataProtector.Hash(string)"/>
    /// to derive deterministic search hashes. Required only when you look up encrypted
    /// fields by equality; supply it via User Secrets, an environment variable, or a
    /// vault (never commit it). When unset, <c>Hash</c> throws.
    /// </summary>
    public string? SearchHashKey { get; set; }
}
