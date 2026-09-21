using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Modulus.Mediator.Behaviors;

/// <summary>
/// Wraps command handling in a database transaction across every *distinct*
/// <see cref="DbContext"/> resolved from the current service scope (duplicate
/// registrations of the same instance are collapsed by type), not just the
/// first one. This replaces the previous behaviour which began a
/// transaction on only the *first* registered <see cref="DbContext"/>,
/// leaving additional contexts (e.g. cross-module writes in a modular
/// monolith) without transactional protection.
/// </summary>
/// <remarks>
/// <para>
/// <b>How it works:</b> A separate EF Core transaction is started on every
/// resolved <see cref="DbContext"/> before the handler runs. If the handler
/// succeeds, all transactions are committed; if it throws, all are rolled
/// back.
/// </para>
/// <para>
/// <b>Domain event dispatch timing:</b> Domain events that ModuleDbContext
/// collected are deferred when explicit transactions are active (they would
/// otherwise dispatch before commit). The deferred queue is drained by the
/// transaction interceptor (DeferredDomainEventTransactionInterceptor) the
/// moment the first wrapped transaction commits, so handlers see consistent,
/// committed state. This behavior drains the queue again after its own commits
/// as a safety net — by then the interceptor has emptied it, so events
/// dispatch exactly once. Manual (non-mediator) transactions are covered by
/// the same interceptor.
/// </para>
/// <para>
/// <b>Single-context case (most common):</b> Fully atomic — one transaction,
/// one commit or rollback.
/// </para>
/// <para>
/// <b>Multi-context caveat:</b> Each context participates in its own
/// independent database transaction. True cross-connection atomicity would
/// require a two-phase commit / distributed-transaction coordinator
/// (MSDTC, Windows-only). If the handler succeeds but a later
/// <c>CommitAsync</c> fails, already-committed contexts cannot be undone.
/// For genuine cross-module consistency prefer the transactional-outbox
/// pattern rather than multi-context transactions.
/// </para>
/// <para>
/// <b>Connection resilience:</b> the whole unit is driven through the first
/// context's <see cref="IExecutionStrategy"/>
/// (<c>CreateExecutionStrategy().ExecuteAsync(...)</c>). This is the pattern EF
/// Core mandates when <c>EnableRetryOnFailure</c> is configured — a manual
/// (user-initiated) transaction throws otherwise. When retry is off (e.g.
/// SQLite) the strategy is a passthrough that runs the delegate once, so the
/// wrapping is always safe. On a transient failure the strategy rolls the
/// transaction back and re-invokes the handler, so handler bodies must be
/// safe to re-run. The shared deferred domain-event queue is discarded at the
/// start of each attempt so a re-run cannot double-dispatch the rolled-back
/// attempt's events.
/// </para>
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IServiceProvider sp,
    TransactionRuntimeOptions options)
    : IPipelineBehavior<TRequest, TResponse>
{
    // Attribute reads are per-request-type and never change, so resolve them once
    // per closed generic instead of reflecting on every command.
    private static readonly bool s_skip =
        typeof(TRequest).GetCustomAttribute<SkipTransactionAttribute>() is not null;

    private static readonly Type[] s_declaredContexts =
        typeof(TRequest).GetCustomAttribute<TransactionalAttribute>()?.Contexts ?? [];

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        // Queries and commands opted out via [SkipTransaction] bypass wrapping.
        if (s_skip || request is IQuery<TResponse>)
            return await next();

        // Resolve every DbContext in scope, deduplicated by runtime type:
        // AddModuleDatabase / AddOutbox / AddInbox each add their own
        // scoped <c>DbContext</c> descriptor resolving the *same* instance, so
        // the raw registration count (2–3) would defeat the TouchedOrSingle
        // mode and make a "multi-context" command out of every single-module
        // write. Only genuinely distinct context types participate.
        var contexts = sp.GetServices<DbContext>()
            .GroupBy(ctx => ctx.GetType())
            .Select(group => group.First())
            .ToList();
        var toWrap = SelectContexts(contexts, typeof(TRequest).Name);
        if (toWrap.Count == 0)
            return await next();

        contexts = toWrap;

        // Drive the unit through an execution strategy so retrying providers
        // (EnableRetryOnFailure) support the manual transaction; without this
        // EF throws "the configured execution strategy does not support
        // user-initiated transactions" at SaveChanges time. For non-retrying
        // providers the strategy simply runs the delegate once.
        var strategy = contexts[0].Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Each execution-strategy retry re-invokes this delegate after
            // rolling back the previous attempt. Events that the failed
            // attempt queued into the shared deferred queue are NOT cleared
            // by the rollback — the re-run handler would enqueue them a
            // second time and the post-commit drain would dispatch every
            // event twice. Discard the rolled-back attempt's leftovers first
            // (a no-op on the first attempt, when the queue is empty).
            sp.GetService<IDeferredDomainEventQueue>()?.DequeueAll();

            // Start a transaction on *every* wrapped context so all writes are
            // protected. The begin loop runs inside the try: if opening
            // context #2 fails, context #1's already-open transaction is still
            // rolled back — begun outside, it would leak until disposal.
            var transactions = new List<IDbContextTransaction>(contexts.Count);
            try
            {
                foreach (var ctx in contexts)
                    transactions.Add(await ctx.Database.BeginTransactionAsync(ct));

                var result = await next();

                // Commit once the handler has succeeded. Multi-context commits are
                // sequential and non-atomic: if a later commit fails, earlier ones
                // cannot be undone — prefer the transactional outbox for
                // cross-module consistency.
                try
                {
                    foreach (var tx in transactions)
                        await tx.CommitAsync(ct);
                }
                catch (Exception commitEx)
                {
                    sp.GetService<ILogger<TransactionBehavior<TRequest, TResponse>>>()?.LogError(
                        commitEx,
                        "Transaction commit failed for {Request} after partial commits; already-committed contexts cannot be rolled back.",
                        typeof(TRequest).Name);
                    throw;
                }

                // Safety net: the transaction interceptor drains the deferred
                // queue at the first commit; this post-commit drain is a no-op
                // when it did, and still dispatches for contexts whose provider
                // path bypasses interceptors.
                await DispatchDeferredDomainEventsAsync(ct);

                return result;
            }
            catch
            {
                // Best-effort rollback of every active transaction.
                foreach (var tx in transactions)
                {
                    try { await tx.RollbackAsync(ct); }
                    catch (Exception) { /* connection may already be broken */ }
                }
                throw;
            }
            finally
            {
                foreach (var tx in transactions)
                {
                    try { await tx.DisposeAsync(); }
                    catch (Exception) { /* a failed dispose must not mask the result */ }
                }
            }
        });
    }

    /// <summary>
    /// Narrows the registered contexts to the ones this command should wrap:
    /// <list type="bullet">
    /// <item>[Transactional(types)] present → exactly those context types.</item>
    /// <item><see cref="TransactionMode.AllContexts"/> → every context (legacy).</item>
    /// <item>otherwise (default) → the single registered context; when several
    /// exist an <see cref="InvalidOperationException"/> is thrown so the missing
    /// intent is fixed instead of silently running without a transaction.</item>
    /// </list>
    /// </summary>
    private List<DbContext> SelectContexts(List<DbContext> contexts, string requestName)
    {
        if (contexts.Count == 0)
            return contexts;

        if (s_declaredContexts.Length > 0)
            return contexts
                .Where(c => Array.Exists(s_declaredContexts, t => t.IsInstanceOfType(c)))
                .ToList();

        if (options.Mode == TransactionMode.AllContexts)
            return contexts;

        // TouchedOrSingle: wrap the one context if unambiguous, else fail fast.
        if (contexts.Count == 1)
            return contexts;

        throw new InvalidOperationException(
            $"Command {requestName} touches an ambiguous transaction scope: {contexts.Count} DbContexts " +
            $"({string.Join(", ", contexts.Select(c => c.GetType().Name))}) are registered but the command " +
            "declares none. Add [Transactional(typeof(...))] to wrap specific contexts, " +
            "opt into TransactionMode.AllContexts, or add [SkipTransaction] when no transaction is intended.");
    }

    /// <summary>
    /// Safety-net drain of the deferred domain-event queue. The transaction
    /// interceptor (registered by AddModuleDatabase) already drains and
    /// dispatches when a transaction commits; this catches the rare provider
    /// paths that bypass interceptors. Draining twice is harmless — the queue
    /// clears on read, so events dispatch exactly once.
    /// </summary>
    private async Task DispatchDeferredDomainEventsAsync(CancellationToken ct)
    {
        var queue = sp.GetService<IDeferredDomainEventQueue>();
        if (queue is null) return;

        var events = queue.DequeueAll();
        if (events.Count == 0) return;

        var dispatcher = sp.GetService<DomainEventDispatcher>();
        if (dispatcher is null) return;

        await dispatcher.DispatchAsync(events, ct);
    }
}
