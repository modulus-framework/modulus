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
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IServiceProvider sp)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        // Queries and commands opted out via [SkipTransaction] bypass wrapping.
        if (request is IQuery<TResponse>
            || typeof(TRequest).GetCustomAttribute<SkipTransactionAttribute>() is not null)
            return await next();

        // Resolve every DbContext in scope; if there are none there is nothing
        // to make transactional.
        var contexts = sp.GetServices<DbContext>().ToList();
        if (contexts.Count == 0)
            return await next();

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
    }
}
