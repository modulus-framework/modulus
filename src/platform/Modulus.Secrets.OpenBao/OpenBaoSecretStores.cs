namespace Modulus.Secrets.OpenBao;

using Modulus.Core.Abstractions.Secrets;
using Modulus.Secrets;

/// <summary>The runtime read path. Callers reach it only through the cached <see cref="ISecretStore"/>.</summary>
internal sealed class OpenBaoSecretStore(OpenBaoKvClient client) : ISecretBackend
{
    /// <inheritdoc />
    public Task<SecretValue> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return client.ReadAsync(reference, cancellationToken);
    }
}

/// <summary>The provisioning write path, bound to the provisioning identity.</summary>
internal sealed class OpenBaoSecretWriter(OpenBaoKvClient client) : ISecretWriter
{
    /// <inheritdoc />
    public Task<int> WriteAsync(
        SecretReference reference,
        IReadOnlyDictionary<string, string> data,
        int? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(data);
        return client.WriteAsync(reference, data, expectedVersion, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int?> GetCurrentVersionAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return client.GetCurrentVersionAsync(reference, cancellationToken);
    }
}
