namespace Modulus.Core.Abstractions.Secrets;

using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Reads secrets from the secret manager. Callers depend on this contract, not on a provider: the framework supplies a
/// cached implementation that wraps the configured provider.
/// </summary>
public interface ISecretStore
{
    /// <summary>Returns the current version of the secret at <paramref name="reference"/>.</summary>
    /// <exception cref="SecretNotFoundException">The secret does not exist.</exception>
    /// <exception cref="SecretStoreUnavailableException">The store cannot be reached and no bounded stale value is available.</exception>
    Task<SecretValue> GetAsync(SecretReference reference, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes secrets. Only the provisioning identity registers this; the runtime API host keeps the read-only identity and
/// must not resolve it.
/// </summary>
public interface ISecretWriter
{
    /// <summary>
    /// Writes a new version. When <paramref name="expectedVersion"/> is given the write succeeds only if the current
    /// version matches (check-and-set); a mismatch throws <see cref="Exceptions.ConflictException"/>.
    /// </summary>
    /// <returns>The new version number.</returns>
    Task<int> WriteAsync(
        SecretReference reference,
        IReadOnlyDictionary<string, string> data,
        int? expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current version, or <c>null</c> when the secret does not exist.</summary>
    Task<int?> GetCurrentVersionAsync(SecretReference reference, CancellationToken cancellationToken = default);
}
