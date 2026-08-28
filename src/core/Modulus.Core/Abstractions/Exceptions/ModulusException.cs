namespace Modulus.Core.Abstractions.Exceptions;

/// <summary>Base for all Modulus framework exceptions.</summary>
public abstract class ModulusException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Thrown when the module dependency graph contains a cycle. The message
/// includes the full cycle path (e.g. <c>A -> B -> C -> A</c>).
/// </summary>
public sealed class CircularDependencyException(IReadOnlyList<Type> cycle)
    : ModulusException(
        $"Circular module dependency detected: {string.Join(" -> ", cycle.Select(t => t.Name))}. " +
        "Break the cycle by removing one of these [DependsOn] declarations.")
{
    /// <summary>The module types forming the cycle, in order (first repeats last).</summary>
    public IReadOnlyList<Type> Cycle { get; } = cycle;
}

public sealed class ModuleNotFoundException(Type dependencyType, Type? declaringModule = null)
    : ModulusException(BuildMessage(dependencyType, declaringModule))
{
    public Type DependencyType { get; } = dependencyType;

    private static string BuildMessage(Type dep, Type? declaring)
    {
        var target = $"'{dep.FullName}'";
        return declaring is null
            ? $"Module {target} is declared as a module dependency but was never registered. " +
              $"Register it via AddModule<{dep.Name}>() or make it reachable from the startup " +
              "module's [DependsOn] graph."
            : $"Module '{declaring.FullName}' depends on {target}, which was never registered. " +
              $"Register it via AddModule<{dep.Name}>() or make it reachable from the startup " +
              "module's [DependsOn] graph.";
    }
}

/// <summary>
/// Thrown when a <see cref="DependsOnAttribute"/> declares a dependency on a type
/// that cannot serve as a module (not an <see cref="IModule"/>, or abstract and
/// therefore not instantiable).
/// </summary>
public sealed class InvalidModuleDependencyException(Type declaringModule, Type dependencyType, string reason)
    : ModulusException(
        $"{declaringModule.FullName} declares a dependency on {dependencyType.FullName}: {reason}. " +
        "[DependsOn] types must be concrete classes implementing IModule.")
{
    public Type DeclaringModule { get; } = declaringModule;
    public Type DependencyType { get; } = dependencyType;
}

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
