namespace Modulus.GraphQL;

using global::GraphQL;
using global::GraphQL.DataLoader;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions;

/// <summary>Resolver helpers: the mediator, DataLoader batching and typed arguments, from the request's scope.</summary>
public static class GraphQLResolveContextExtensions
{
    /// <summary>
    /// Runs <paramref name="query"/> through the request's mediator, so the pipeline behaviours apply
    /// (validation, logging, <c>[CacheFor]</c> caching).
    /// </summary>
    public static Task<TResult> QueryAsync<TResult>(this IResolveFieldContext context, IQuery<TResult> query)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Services(context).GetRequiredService<IMediator>().QueryAsync(query, context.CancellationToken);
    }

    /// <summary>
    /// Sends <paramref name="command"/> through the request's mediator (validation, transaction, cache invalidation).
    /// </summary>
    public static Task<TResult> SendAsync<TResult>(this IResolveFieldContext context, ICommand<TResult> command)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Services(context).GetRequiredService<IMediator>().SendAsync(command, context.CancellationToken);
    }

    /// <summary>
    /// Loads <paramref name="key"/> through the request's batch loader <paramref name="loaderName"/>: every key asked
    /// for while the current level of the query resolves is fetched with one <paramref name="fetch"/> call (keys
    /// distinct). A key missing from the result resolves to <c>default</c>.
    /// </summary>
    public static IDataLoaderResult<TValue> LoadBatch<TKey, TValue>(
        this IResolveFieldContext context,
        string loaderName,
        TKey key,
        Func<IReadOnlyList<TKey>, IServiceProvider, CancellationToken, Task<IDictionary<TKey, TValue>>> fetch)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(loaderName);
        ArgumentNullException.ThrowIfNull(fetch);

        var services = Services(context);
        var loaders = services.GetRequiredService<IDataLoaderContextAccessor>().Context
            ?? throw new InvalidOperationException("No DataLoader context: AddModulusGraphQL registers it for GraphQL requests.");
        var loader = loaders.GetOrAddBatchLoader<TKey, TValue>(loaderName, (keys, ct) => fetch(keys.ToList(), services, ct));
        return loader.LoadAsync(key);
    }

    /// <summary>
    /// The <paramref name="name"/> argument (an <c>ID</c>) as a <see cref="Guid"/>; anything else is a validation
    /// error (<c>VALIDATION_FAILED</c>), not a server error.
    /// </summary>
    public static Guid GetGuidArgument(this IResolveFieldContext context, string name)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.GetArgument<object?>(name);
        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => throw new ValidationException([$"{name}: must be a GUID"]),
        };
    }

    private static IServiceProvider Services(IResolveFieldContext context)
        => context.RequestServices
           ?? throw new InvalidOperationException("The GraphQL request has no service provider (ExecutionOptions.RequestServices).");
}
