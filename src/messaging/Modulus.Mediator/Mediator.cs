namespace Modulus.Mediator;

using System.Reflection;
using Modulus.Mediator.Abstractions;

internal sealed class Mediator(IServiceProvider sp) : IMediator
{
    public Task<TResponse> SendAsync<TResponse>(
        ICommand<TResponse> command, CancellationToken ct)
        => DispatchAsync<TResponse>(command, ct);

    public Task<TResponse> QueryAsync<TResponse>(
        IQuery<TResponse> query, CancellationToken ct)
        => DispatchAsync<TResponse>(query, ct);

    private async Task<TResponse> DispatchAsync<TResponse>(
        object request, CancellationToken ct)
    {
        var requestType = request.GetType();

        // Build innermost handler delegate
        RequestHandlerDelegate<TResponse> handler = () =>
        {
            // Resolve the handler by the marker interface the request actually
            // implements. Building ICommandHandler<,> for an IQuery used to
            // throw an ArgumentException because TCommand is constrained to
            // ICommand<TResponse> — the previous "try command then query"
            // MakeGenericType evaluated eagerly and never reached the fallback.
            Type handlerType;
            if (request is ICommand<TResponse>)
                handlerType = typeof(ICommandHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            else if (request is IQuery<TResponse>)
                handlerType = typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            else
                throw new InvalidOperationException(
                    $"Request type {requestType.Name} implements neither " +
                    $"ICommand<{typeof(TResponse).Name}> nor IQuery<{typeof(TResponse).Name}>.");

            dynamic h = sp.GetRequiredService(handlerType);
            return h.HandleAsync((dynamic)request, ct);
        };

        // Resolve behaviors for the ACTUAL request type (not 'object')
        var behaviorInterface = typeof(IPipelineBehavior<,>)
            .MakeGenericType(requestType, typeof(TResponse));

        var behaviors = ((IEnumerable<object>?)
            sp.GetService(
                typeof(IEnumerable<>).MakeGenericType(behaviorInterface))
            ?? [])
            .Reverse()
            .ToList();

        // Wrap with behaviors (reverse order = outermost first at execution)
        foreach (var behavior in behaviors)
        {
            var next = handler;
            dynamic b = behavior;
            handler = () => b.HandleAsync((dynamic)request, next, ct);
        }

        return await handler();
    }
}
