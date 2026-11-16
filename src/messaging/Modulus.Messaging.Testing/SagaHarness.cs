using Modulus.Messaging.Sagas;

namespace Modulus.Messaging.Testing;

/// <summary>
/// Reads and waits for one saga type's instances in a running <see cref="MessagingTestHarness"/> (FR-25): the saga's state, and
/// whether it has ended. Waits poll the repository, so they work with the real clock and with a <see cref="VirtualClock"/>.
/// </summary>
public sealed class SagaHarness<TSaga>(MessagingTestHarness harness)
    where TSaga : SagaInstance
{
    private readonly ISagaRepository<TSaga> _repository = harness.Services.GetRequiredService<ISagaRepository<TSaga>>();

    /// <summary>The stored instance for a correlation key, or null when it has ended or never existed.</summary>
    public Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken = default) =>
        _repository.FindAsync(correlationId, cancellationToken);

    /// <summary>Waits until the saga for the key is in <paramref name="state"/>. Returns whether it got there within the timeout.</summary>
    public Task<bool> WaitForStateAsync(Guid correlationId, string state, TimeSpan? timeout = null) =>
        WaitUntilAsync(async () => (await _repository.FindAsync(correlationId, CancellationToken.None).ConfigureAwait(false))?.CurrentState == state, timeout);

    /// <summary>Waits until the saga for the key has ended (its instance was removed). Returns whether it ended within the timeout.</summary>
    public Task<bool> WaitForCompletionAsync(Guid correlationId, TimeSpan? timeout = null) =>
        WaitUntilAsync(async () => await _repository.FindAsync(correlationId, CancellationToken.None).ConfigureAwait(false) is null, timeout);

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return await condition().ConfigureAwait(false);
    }
}
