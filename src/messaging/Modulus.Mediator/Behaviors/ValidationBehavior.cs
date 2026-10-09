using System.Reflection;

namespace Modulus.Mediator.Behaviors;

using FluentValidation;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;
using ValidationException = Modulus.Core.Abstractions.Exceptions.ValidationException;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
{
    // One attribute lookup per request type for the process, not per request.
    private static readonly bool s_skip = typeof(TRequest).GetCustomAttribute<SkipValidationAttribute>() is not null;

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (s_skip)
            return await next();

        // Resolved as an array/list in practice; avoid enumerating a lazy sequence several times.
        var all = validators as IReadOnlyList<IValidator<TRequest>> ?? validators.ToArray();
        if (all.Count == 0)
            return await next();

        var ctx = new ValidationContext<TRequest>(request);

        // Use ValidateAsync so async FluentValidation rules (e.g. DB uniqueness
        // checks) execute correctly. Sync Validate() silently skips them.
        var results = all.Count == 1
            ? [await all[0].ValidateAsync(ctx, ct)]
            : await Task.WhenAll(all.Select(v => v.ValidateAsync(ctx, ct)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(e => e is not null)
            .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
