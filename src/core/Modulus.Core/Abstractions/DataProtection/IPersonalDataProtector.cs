namespace Modulus.Core.Abstractions.DataProtection;

/// <summary>
/// Encrypts and decrypts personal-data values at rest and derives deterministic
/// search hashes for them. The default implementation is backed by ASP.NET Core Data
/// Protection (key ring with rotation); register your own before
/// <c>AddModulusPersonalDataProtection</c> to override it.
/// </summary>
public interface IPersonalDataProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/>. The result is
    /// non-deterministic (the same input yields different ciphertext each call), so it
    /// cannot be used for equality search — see <see cref="Hash(string)"/>.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a value produced by <see cref="Protect(string)"/>. Values
    /// encrypted under a now-retired key still decrypt as long as that key remains in
    /// the ring, so key rotation does not require re-encrypting existing rows.</summary>
    string Unprotect(string ciphertext);

    /// <summary>
    /// Derives a stable, deterministic keyed hash (HMAC) of <paramref name="value"/>
    /// so an encrypted column can still be looked up by equality: store the hash in a
    /// companion column and query on it. Unlike <see cref="Protect(string)"/> the
    /// output is identical for identical input.
    /// </summary>
    string Hash(string value);
}
