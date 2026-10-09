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
        var name = RequestName;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        MediatorLog.Handling(logger, name, null);
        try
        {
            var result = await next();
            if (logger.IsEnabled(LogLevel.Debug))
                MediatorLog.Handled(logger, name, (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, null);
            return result;
        }
        catch (Exception ex)
        {
            MediatorLog.Failed(logger, name, (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds, ex);
            throw;
        }
    }

    private static readonly string RequestName = typeof(TRequest).Name;
}

internal static class MediatorLog
{
    public static readonly Action<ILogger, string, Exception?> Handling =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, nameof(Handling)), "Handling {Request}");

    public static readonly Action<ILogger, string, long, Exception?> Handled =
        LoggerMessage.Define<string, long>(LogLevel.Debug, new EventId(2, nameof(Handled)), "Handled {Request} in {Ms}ms");

    public static readonly Action<ILogger, string, long, Exception?> Failed =
        LoggerMessage.Define<string, long>(LogLevel.Error, new EventId(3, nameof(Failed)), "Error handling {Request} after {Ms}ms");
}
