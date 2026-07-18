using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Modulus.Mediator.Behaviors;

/// <summary>
/// Wraps command handling in a database transaction across every
/// <see cref="DbContext"/> resolved from the current service scope, not just
/// the first one. This replaces the previous behaviour which began a
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
/// safe to re-run.
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

        // Resolve every DbContext in scope, then narrow to the ones this command
        // actually needs so we don't open a transaction on every module context.
        var contexts = sp.GetServices<DbContext>().ToList();
        var toWrap = SelectContexts(contexts);
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
            // Start a transaction on *every* context so all writes are protected,
            // not just those on the first registered DbContext.
            var transactions = new List<IDbContextTransaction>(contexts.Count);
            foreach (var ctx in contexts)
                transactions.Add(await ctx.Database.BeginTransactionAsync(ct));

            try
            {
                var result = await next();

                // Commit once the handler has succeeded.
                foreach (var tx in transactions)
                    await tx.CommitAsync(ct);

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
                    await tx.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// Narrows the registered contexts to the ones this command should wrap:
    /// <list type="bullet">
    /// <item>[Transactional(types)] present → exactly those context types.</item>
    /// <item><see cref="TransactionMode.AllContexts"/> → every context (legacy).</item>
    /// <item>otherwise (default) → the single registered context, or none when
    /// several exist (each SaveChanges is atomic on its own; declare intent with
    /// [Transactional] to wrap specific contexts).</item>
    /// </list>
    /// </summary>
    private List<DbContext> SelectContexts(List<DbContext> contexts)
    {
        if (contexts.Count == 0)
            return contexts;

        if (s_declaredContexts.Length > 0)
            return contexts
                .Where(c => Array.Exists(s_declaredContexts, t => t.IsInstanceOfType(c)))
                .ToList();

        if (options.Mode == TransactionMode.AllContexts)
            return contexts;

        // TouchedOrSingle: wrap the one context if unambiguous, else nothing.
        return contexts.Count == 1 ? contexts : [];
    }
}
