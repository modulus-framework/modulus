namespace Modulus.Secrets;

using System.Collections.Concurrent;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;

/// <summary>
/// Test and single-process backend: values in memory, with versions and check-and-set writes. The in-memory store check
/// flags it in production unless the contract is allow-listed.
/// </summary>
public sealed class InMemorySecretStore : ISecretBackend, ISecretWriter
{
    private readonly ConcurrentDictionary<string, SecretValue> _values = new(StringComparer.Ordinal);
    private readonly object _writeGate = new();

    /// <inheritdoc />
    public Task<SecretValue> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        return _values.TryGetValue(reference.Path, out var value)
            ? Task.FromResult(value)
            : throw new SecretNotFoundException("The secret was not found.");
    }

    /// <inheritdoc />
    public Task<int> WriteAsync(
        SecretReference reference,
        IReadOnlyDictionary<string, string> data,
        int? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_writeGate)
        {
            var current = _values.TryGetValue(reference.Path, out var existing) ? existing.Version : (int?)null;

            // Same rule as OpenBao's cas: 0 means "create only" (an absent secret is version 0), n means "current version is n".
            if (expectedVersion is { } expected && expected != (current ?? 0))
                throw new ConflictException("The secret was changed by another writer; reload and retry.");

            var next = (current ?? 0) + 1;
            _values[reference.Path] = new SecretValue(new Dictionary<string, string>(data, StringComparer.Ordinal), next);
            return Task.FromResult(next);
        }
    }

    /// <inheritdoc />
    public Task<int?> GetCurrentVersionAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_values.TryGetValue(reference.Path, out var value) ? (int?)value.Version : null);
    }
}
