using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Mediator.Behaviors;

using Microsoft.EntityFrameworkCore;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

public sealed class TransactionBehavior<TRequest, TResponse>(
    IServiceProvider sp)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        // Queries and commands with SkipTransaction bypass this
        if (request is IQuery<TResponse>
            || typeof(TRequest).GetCustomAttribute<SkipTransactionAttribute>() is not null)
            return await next();

        // Resolve all DbContexts registered in scope and wrap in transaction
        var contexts = sp.GetServices<DbContext>().ToList();

        if (contexts.Count == 0) return await next();

        // Use the first context to drive the transaction
        var primary = contexts[0];

        await using var tx = await primary.Database
            .BeginTransactionAsync(ct);
        try
        {
            var result = await next();
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}