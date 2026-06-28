using FluentValidation;

namespace Modulus.AspNetCore.Validation;

/// <summary>
/// Base class for request validators.  Register in DI with
/// <c>AddValidatorsFromAssembly</c> or register individually.
/// The endpoint pipeline automatically discovers and invokes
/// the validator matching the request type.
/// </summary>
/// <typeparam name="T">The request type to validate.</typeparam>
public abstract class Validator<T> : AbstractValidator<T>
{
}
