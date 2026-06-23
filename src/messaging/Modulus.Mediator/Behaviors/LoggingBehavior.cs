using Microsoft.Extensions.Logging;

namespace Modulus.Mediator.Behaviors;

using Modulus.Mediator.Abstractions;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var sw   = System.Diagnostics.Stopwatch.StartNew();
        logger.LogDebug("Handling {Request}", name);
        try
        {
            var result = await next();
            logger.LogDebug("Handled {Request} in {Ms}ms", name, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling {Request} after {Ms}ms",
                name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}