using System.Text.Json;

namespace Modulus.Messaging.Sagas;

/// <summary>
/// Keeps instances in memory as JSON, under one lock. Each read is a copy, so a handler that fails half way leaves the
/// stored instance unchanged. A save or delete checks the version the caller read (optimistic concurrency).
/// </summary>
public sealed class InMemorySagaRepository<TSaga> : ISagaRepository<TSaga> where TSaga : SagaInstance
{
    private readonly Dictionary<Guid, string> _instances = [];
    private readonly object _gate = new();

    public Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_instances.TryGetValue(correlationId, out var json))
            {
                return Task.FromResult<TSaga?>(null);
            }

            return Task.FromResult(JsonSerializer.Deserialize<TSaga>(json));
        }
    }

    public Task SaveAsync(TSaga saga, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saga);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var storedVersion = StoredVersion(saga.CorrelationId);
            if (storedVersion != saga.Version)
            {
                throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
            }

            saga.Version = storedVersion + 1;
            _instances[saga.CorrelationId] = JsonSerializer.Serialize(saga);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(TSaga saga, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saga);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (StoredVersion(saga.CorrelationId) != saga.Version)
            {
                throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
            }

            _instances.Remove(saga.CorrelationId);
        }

        return Task.CompletedTask;
    }

    /// <summary>The stored version, or 0 when no instance is stored. Call under the lock.</summary>
    private long StoredVersion(Guid correlationId) =>
        _instances.TryGetValue(correlationId, out var json)
            ? JsonSerializer.Deserialize<TSaga>(json)!.Version
            : 0;
}
