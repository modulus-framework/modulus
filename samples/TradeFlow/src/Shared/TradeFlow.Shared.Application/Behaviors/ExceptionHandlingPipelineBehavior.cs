using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulus.Mediator.Abstractions;

namespace TradeFlow.Shared.Application.Behaviors;

internal sealed class ExceptionHandlingPipelineBehavior<TRequest, TResponse>(
    ILogger<ExceptionHandlingPipelineBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogError(exception, "Database concurrency conflict for {RequestName}", typeof(TRequest).Name);

            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception for {RequestName}", typeof(TRequest).Name);

            throw;
        }
    }
}
