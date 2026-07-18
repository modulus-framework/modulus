namespace Modulus.Mediator;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Modulus.Mediator.Abstractions;

internal sealed class Mediator(IServiceProvider sp) : IMediator
{
    // Cached handler delegates: requestType → Func<handler, request, ct, Task<TResponse>>
    private static readonly ConcurrentDictionary<Type, Delegate> s_handlerDelegates = new();

    // Cached behavior delegates: (behaviorType, requestType, responseType) → compiled invoke
    private static readonly ConcurrentDictionary<(Type, Type, Type), Delegate> s_behaviorDelegates = new();

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

        // Build innermost handler delegate using compiled expression tree
        RequestHandlerDelegate<TResponse> handler = () =>
        {
            Type handlerType;
            if (request is ICommand<TResponse>)
                handlerType = typeof(ICommandHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            else if (request is IQuery<TResponse>)
                handlerType = typeof(IQueryHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            else
                throw new InvalidOperationException(
                    $"Request type {requestType.Name} implements neither " +
                    $"ICommand<{typeof(TResponse).Name}> nor IQuery<{typeof(TResponse).Name}>.");

            var h = sp.GetRequiredService(handlerType);
            var invoke = (Func<object, object, CancellationToken, Task<TResponse>>)
                s_handlerDelegates.GetOrAdd(handlerType,
                    static t => CompileHandlerDelegate<TResponse>(t));
            return invoke(h, request, ct);
        };

        // Resolve behaviors for the ACTUAL request type
        var behaviorInterface = typeof(IPipelineBehavior<,>)
            .MakeGenericType(requestType, typeof(TResponse));

        var behaviors = ((IEnumerable<object>?)
            sp.GetService(typeof(IEnumerable<>).MakeGenericType(behaviorInterface))
            ?? [])
            .Reverse()
            .ToList();

        // Wrap with behaviors (reverse order = outermost first at execution)
        foreach (var behavior in behaviors)
        {
            var next = handler;
            var bType = behavior.GetType();
            var invoke = (Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>>)
                s_behaviorDelegates.GetOrAdd((bType, requestType, typeof(TResponse)),
                    static key => CompileBehaviorDelegate<TResponse>(key.Item1, key.Item2));
            var captured = behavior;
            handler = () => invoke(captured, request, next, ct);
        }

        return await handler();
    }

    private static Func<object, object, CancellationToken, Task<TResponse>>
        CompileHandlerDelegate<TResponse>(Type handlerType)
    {
        var method = handlerType.GetMethod(
            "HandleAsync", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Handler '{handlerType.FullName}' has no public HandleAsync method.");

        var requestType = method.GetParameters()[0].ParameterType;

        var handlerParam  = Expression.Parameter(typeof(object), "h");
        var requestParam  = Expression.Parameter(typeof(object), "req");
        var ctParam       = Expression.Parameter(typeof(CancellationToken), "ct");
        var castHandler   = Expression.Convert(handlerParam, handlerType);
        var castRequest   = Expression.Convert(requestParam, requestType);
        var call          = Expression.Call(castHandler, method, castRequest, ctParam);

        return Expression.Lambda<Func<object, object, CancellationToken, Task<TResponse>>>(
            call, handlerParam, requestParam, ctParam).Compile();
    }

    private static Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>>
        CompileBehaviorDelegate<TResponse>(Type behaviorType, Type requestType)
    {
        var method = behaviorType.GetMethod(
            "HandleAsync", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Behavior '{behaviorType.FullName}' has no public HandleAsync method.");

        var behaviorParam = Expression.Parameter(typeof(object), "b");
        var requestParam  = Expression.Parameter(typeof(object), "req");
        var nextParam     = Expression.Parameter(typeof(RequestHandlerDelegate<TResponse>), "next");
        var ctParam       = Expression.Parameter(typeof(CancellationToken), "ct");
        var castBehavior  = Expression.Convert(behaviorParam, behaviorType);
        var castRequest   = Expression.Convert(requestParam, requestType);
        var call          = Expression.Call(castBehavior, method, castRequest, nextParam, ctParam);

        return Expression.Lambda<Func<object, object, RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>>>(
            call, behaviorParam, requestParam, nextParam, ctParam).Compile();
    }
}
