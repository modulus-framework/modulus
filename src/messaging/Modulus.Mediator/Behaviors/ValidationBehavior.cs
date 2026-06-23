using System.ComponentModel.DataAnnotations;
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
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (typeof(TRequest).GetCustomAttribute<SkipValidationAttribute>() is not null
            || !validators.Any())
            return await next();

        var ctx = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(v => v.Validate(ctx))
            .SelectMany(r => r.Errors)
            .Where(e => e is not null)
            .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}