namespace Modulus.Core.Abstractions.Exceptions;

/// <summary>Base for all Modulus framework exceptions.</summary>
public abstract class ModulusException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class CircularDependencyException(Type moduleType)
    : ModulusException($"Circular dependency detected involving module: {moduleType.Name}");

public sealed class ModuleNotFoundException(Type dependencyType)
    : ModulusException(
        $"Module {dependencyType.Name} is declared as a dependency but was not registered. " +
        "Call AddModule<{dependencyType.Name}>() before the dependent module.");

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

public sealed class ConflictException(string message)
    : ModulusException(message);