using Microsoft.Extensions.Logging;
using global::Polly;
using global::Polly.Retry;
using global::Polly.CircuitBreaker;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;

namespace Modulus.Sagas.Resilience;

/// <summary>
/// Rebus incoming pipeline step that wraps handler invocation in a Polly
/// <see cref="ResiliencePipeline"/>.  Provides retry with configurable
/// back-off and optional circuit-breaker protection — replacing Rebus's
/// built-in <c>DefaultRetryStep</c> with the richer Polly v8 API.
/// </summary>
public sealed class PollyRetryStep(
    ResiliencePipeline pipeline,
    ILogger<PollyRetryStep>? logger = null) : IIncomingStep
{
    /// <inheritdoc />
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        var token = context.Load<CancellationToken>();

        try
        {
            await pipeline.ExecuteAsync(async _ =>
            {
                await next();
            }, token);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex,
                "Polly pipeline exhausted retries; propagating to Rebus error handling.");
            throw;
        }
    }
}

/// <summary>
/// Builds a Polly <see cref="ResiliencePipeline"/> from
/// <see cref="PollyRetryOptions"/>.
/// </summary>
public static class PollyPipelineFactory
{
    /// <summary>
    /// Creates a <see cref="ResiliencePipeline"/> configured with retry
    /// and (optionally) circuit-breaker strategies derived from
    /// <paramref name="options"/>.
    /// </summary>
    public static ResiliencePipeline Create(PollyRetryOptions options)
    {
        var builder = new ResiliencePipelineBuilder();

        if (options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.BaseDelay,
                BackoffType = options.DelayBackoffType,
                UseJitter = options.UseJitter,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
            });
        }

        if (options.EnableCircuitBreaker)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.CircuitBreakerFailureRatio,
                SamplingDuration = options.CircuitBreakerSamplingDuration,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                BreakDuration = options.CircuitBreakerBreakDuration,
            });
        }

        return builder.Build();
    }
}
