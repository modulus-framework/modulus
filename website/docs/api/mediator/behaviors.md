---
sidebar_position: 9
---

# Pipeline Behaviors API

## IPipelineBehavior\<TRequest, TResponse\>

```csharp
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct);
}
```

## Built-in Behaviors

### LoggingBehavior

```csharp
public class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
{
    // Logs command/query name and execution time
}
```

### ValidationBehavior

```csharp
public class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
{
    // Validates using FluentValidation validators
    // Throws ValidationException on failure
}
```

### TransactionBehavior

```csharp
public class TransactionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
{
    // Wraps every distinct DbContext (deduped by type) via the EF execution
    // strategy (EnableRetryOnFailure-safe; handlers must be re-runnable).
    // Skips IQuery<T> + [SkipTransaction]; fail-fast when >1 context and no
    // [Transactional(...)]/AllContexts intent; multi-context commits are
    // independent (non-atomic) — prefer the outbox for cross-module consistency.
}
```

### FeatureGateBehavior

```csharp
public class FeatureGateBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
{
    // Gates commands behind feature flags
}
```

## Custom Behavior

```csharp
public sealed class TimingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TimingBehavior<TRequest, TResponse>> _logger;

    public TimingBehavior(ILogger<TimingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> HandleAsync(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        _logger.LogInformation("{Request} completed in {Elapsed}ms",
            typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return response;
    }
}
```
