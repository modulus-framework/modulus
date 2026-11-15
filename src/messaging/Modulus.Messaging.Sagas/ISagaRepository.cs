namespace Modulus.Messaging.Sagas;

/// <summary>
/// Stores saga instances by correlation key, with optimistic concurrency: a save or delete succeeds only when the stored
/// version is the one the caller read. Otherwise it throws <see cref="SagaConcurrencyException"/>.
/// </summary>
public interface ISagaRepository<TSaga> where TSaga : SagaInstance
{
    /// <summary>Returns a copy of the stored instance, or null when there is none.</summary>
    Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken);

    /// <summary>Stores the instance when its version is the stored one, and sets the new version on it.</summary>
    Task SaveAsync(TSaga saga, CancellationToken cancellationToken);

    /// <summary>Removes the instance when its version is the stored one. A saga that completes calls this.</summary>
    Task DeleteAsync(TSaga saga, CancellationToken cancellationToken);
}
