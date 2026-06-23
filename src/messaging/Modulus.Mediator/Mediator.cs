namespace Modulus.Mediator;

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
            // Try ICommandHandler first, then IQueryHandler
            var handlerType =
                typeof(ICommandHandler<,>).MakeGenericType(requestType, typeof(TResponse))
                is var cmdType && sp.GetService(cmdType) is { } cmdHandler
                    ? cmdType
                    : typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));

            dynamic h = sp.GetRequiredService(handlerType);
            return h.HandleAsync((dynamic)request, ct);
        };

        // Wrap with behaviors (reverse order = outermost first at execution)
        var behaviors = sp
            .GetServices<IPipelineBehavior<object, TResponse>>()
            .Reverse()
            .ToList();

        foreach (var behavior in behaviors)
        {
            var next = handler;
            var b    = behavior;
            handler  = () => b.HandleAsync(request, next, ct);
        }

        return await handler();
    }
}