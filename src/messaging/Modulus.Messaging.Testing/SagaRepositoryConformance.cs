using Modulus.Messaging.Sagas;

namespace Modulus.Messaging.Testing;

/// <summary>
/// Runs the contract checks of an <see cref="ISagaRepository{TSaga}"/> against any implementation (NFR-16): reads,
/// optimistic concurrency, copies that stay separate from the stored state, and deletes. Each check starts from a fresh
/// repository and a new correlation id. Use it from a repository's test project:
/// <c>var report = await SagaRepositoryConformance.RunAsync(() => repo, id => new MySaga { CorrelationId = id }, s => s.CurrentState = "Open");</c>
/// then <c>report.EnsurePassed()</c>.
/// </summary>
public static class SagaRepositoryConformance
{
    /// <summary>Runs every check and reports which passed and which failed.</summary>
    /// <param name="createRepository">Returns a repository over an empty store (or a store the checks can ignore).</param>
    /// <param name="newSaga">Creates a new, unsaved instance for a correlation id.</param>
    /// <param name="change">Changes the state of an instance, so a saved change can be told apart from the initial one.</param>
    /// <param name="cancellationToken">Stops the run.</param>
    public static async Task<TransportConformanceReport> RunAsync<TSaga>(
        Func<ISagaRepository<TSaga>> createRepository,
        Func<Guid, TSaga> newSaga,
        Action<TSaga> change,
        CancellationToken cancellationToken = default)
        where TSaga : SagaInstance
    {
        ArgumentNullException.ThrowIfNull(createRepository);
        ArgumentNullException.ThrowIfNull(newSaga);
        ArgumentNullException.ThrowIfNull(change);

        var checks = new (string Name, Func<CancellationToken, Task> Run)[]
        {
            ("find returns null when nothing is stored", ct => FindReturnsNullWhenMissing(createRepository, ct)),
            ("a saved instance is found with its state and version", ct => SavedInstanceIsFound(createRepository, newSaga, change, ct)),
            ("a stale save is refused and the stored state is kept", ct => StaleSaveIsRefused(createRepository, newSaga, change, ct)),
            ("a second insert of the same saga is refused", ct => SecondInsertIsRefused(createRepository, newSaga, ct)),
            ("a found copy is separate from the stored state", ct => FoundCopyIsSeparate(createRepository, newSaga, change, ct)),
            ("delete removes the instance", ct => DeleteRemovesTheInstance(createRepository, newSaga, change, ct)),
            ("a stale delete is refused", ct => StaleDeleteIsRefused(createRepository, newSaga, change, ct)),
        };

        var passed = new List<string>();
        var failed = new List<string>();
        foreach (var (name, run) in checks)
        {
            try
            {
                await run(cancellationToken).ConfigureAwait(false);
                passed.Add(name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed.Add($"{name}: {ex.Message}");
            }
        }

        return new TransportConformanceReport(passed, failed);
    }

    private static async Task FindReturnsNullWhenMissing<TSaga>(Func<ISagaRepository<TSaga>> create, CancellationToken ct)
        where TSaga : SagaInstance
    {
        var repository = create();
        var found = await repository.FindAsync(Guid.NewGuid(), ct).ConfigureAwait(false);
        Check(found is null, "a saga that was never saved was found");
    }

    private static async Task SavedInstanceIsFound<TSaga>(Func<ISagaRepository<TSaga>> create, Func<Guid, TSaga> newSaga, Action<TSaga> change, CancellationToken ct)
        where TSaga : SagaInstance
    {
        var repository = create();
        var id = Guid.NewGuid();
        var saga = newSaga(id);
        change(saga);
        await repository.SaveAsync(saga, ct).ConfigureAwait(false);

        var found = await repository.FindAsync(id, ct).ConfigureAwait(false);
        Check(found is not null, "the saved saga was not found");
        Check(found!.CurrentState == saga.CurrentState, $"the state was '{found.CurrentState}', expected '{saga.CurrentState}'");
        Check(found.Version == saga.Version && found.Version > 0, $"the version was {found.Version}, expected {saga.Version}");
    }

    private static async Task StaleSaveIsRefused<TSaga>(Func<ISagaRepository<TSaga>> create, Func<Guid, TSaga> newSaga, Action<TSaga> change, CancellationToken ct)
        where TSaga : SagaInstance
    {
        var repository = create();
        var id = Guid.NewGuid();
        var saga = newSaga(id);
        await repository.SaveAsync(saga, ct).ConfigureAwait(false);

        var first = (await repository.FindAsync(id, ct).ConfigureAwait(false))!;
        var second = (await repository.FindAsync(id, ct).ConfigureAwait(false))!;
        change(first);
        await repository.SaveAsync(first, ct).ConfigureAwait(false);

        change(second);
        var stale = await ThrowsConcurrencyAsync(() => repository.SaveAsync(second, ct)).ConfigureAwait(false);
        Check(stale, "a save of a copy that was changed meanwhile was accepted");
        var stored = await repository.FindAsync(id, ct).ConfigureAwait(false);
        Check(stored!.Version == first.Version, "the stale save changed the stored version");
        Check(stored.CurrentState == first.CurrentState, "the stale save changed the stored state");
    }

    private static async Task SecondInsertIsRefused<TSaga>(Func<ISagaRepository<TSaga>> create, Func<Guid, TSaga> newSaga, CancellationToken ct)
        where TSaga : SagaInstance
    {
        var repository = create();
        var id = Guid.NewGuid();
        await repository.SaveAsync(newSaga(id), ct).ConfigureAwait(false);

        var duplicate = newSaga(id);
        var refused = await ThrowsConcurrencyAsync(() => repository.SaveAsync(duplicate, ct)).ConfigureAwait(false);
        Check(refused, "a second insert of an existing saga was accepted");
    }

    private static async Task FoundCopyIsSeparate<TSaga>(Func<ISagaRepository<TSaga>> create, Func<Guid, TSaga> newSaga, Action<TSaga> change, CancellationToken ct)
        where TSaga : SagaInstance
    {
        var repository = create();
        var id = Guid.NewGuid();
        var saga = newSaga(id);
        change(saga);
        await repository.SaveAsync(saga, ct).ConfigureAwait(false);
        var storedState = saga.CurrentState;

        var copy = (await repository.FindAsync(id, ct).ConfigureAwait(false))!;
        copy.CurrentState = "Mutated without a save";

        var again = await repository.FindAsync(id, ct).ConfigureAwait(false);
        Check(again!.CurrentState == storedState, "a change to a found copy showed up without a save");
    }

    private static async Task DeleteRemovesTheInstance<TSaga>(Func<ISagaRepository<TSaga>> create, Func<Guid, TSaga> newSaga, Action<TSaga> change, CancellationToken ct)
        where TSaga : SagaInstance
    {
        var repository = create();
        var id = Guid.NewGuid();
        var saga = newSaga(id);
        change(saga);
        await repository.SaveAsync(saga, ct).ConfigureAwait(false);

        var found = (await repository.FindAsync(id, ct).ConfigureAwait(false))!;
        await repository.DeleteAsync(found, ct).ConfigureAwait(false);

        Check(await repository.FindAsync(id, ct).ConfigureAwait(false) is null, "the deleted saga was still found");
    }

    private static async Task StaleDeleteIsRefused<TSaga>(Func<ISagaRepository<TSaga>> create, Func<Guid, TSaga> newSaga, Action<TSaga> change, CancellationToken ct)
        where TSaga : SagaInstance
    {
        var repository = create();
        var id = Guid.NewGuid();
        await repository.SaveAsync(newSaga(id), ct).ConfigureAwait(false);

        var first = (await repository.FindAsync(id, ct).ConfigureAwait(false))!;
        var stale = (await repository.FindAsync(id, ct).ConfigureAwait(false))!;
        change(first);
        await repository.SaveAsync(first, ct).ConfigureAwait(false);

        var refused = await ThrowsConcurrencyAsync(() => repository.DeleteAsync(stale, ct)).ConfigureAwait(false);
        Check(refused, "a delete of a stale copy was accepted");
        Check(await repository.FindAsync(id, ct).ConfigureAwait(false) is not null, "the stale delete removed the saga");
    }

    private static async Task<bool> ThrowsConcurrencyAsync(Func<Task> act)
    {
        try
        {
            await act().ConfigureAwait(false);
            return false;
        }
        catch (SagaConcurrencyException)
        {
            return true;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
