namespace Modulus.Mediator.Abstractions;

public interface IMediator
{
    Task<TResponse> SendAsync<TResponse>(
        ICommand<TResponse> command,
        CancellationToken ct = default);

    Task<TResponse> QueryAsync<TResponse>(
        IQuery<TResponse> query,
        CancellationToken ct = default);
}