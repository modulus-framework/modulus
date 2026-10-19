namespace Modulus.Secrets;

using Modulus.Core.Abstractions.Secrets;

/// <summary>Drops cached secrets, so the next read goes to the store (used after a rotation).</summary>
public interface ISecretCache
{
    /// <summary>Forgets the cached value for <paramref name="reference"/>.</summary>
    void Evict(SecretReference reference);
}
