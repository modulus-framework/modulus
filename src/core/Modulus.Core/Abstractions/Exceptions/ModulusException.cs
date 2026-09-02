namespace Modulus.Core.Abstractions.Exceptions;

/// <summary>Base for all Modulus framework exceptions.</summary>
public abstract class ModulusException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class NotFoundException(string message)
    : ModulusException(message);

public sealed class ValidationException(IEnumerable<string> errors)
    : ModulusException($"Validation failed: {string.Join("; ", errors)}")
{
    public IReadOnlyList<string> Errors { get; }
        = errors.ToList().AsReadOnly();
}

public sealed class UnauthorizedException()
    : ModulusException("Authentication required.");

public sealed class ForbiddenException(string permission)
    : ModulusException($"Access denied. Required permission: {permission}");

public sealed class FeatureDisabledException(string feature)
    : ModulusException($"Feature not available for this tenant: {feature}")
{
    public string Feature { get; } = feature;
}

public sealed class ConflictException(string message)
    : ModulusException(message);
