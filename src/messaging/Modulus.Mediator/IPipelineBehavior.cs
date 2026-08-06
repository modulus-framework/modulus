namespace Modulus.Mediator.Abstractions;

public delegate Task<TResponse>
    RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct);
}
