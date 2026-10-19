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

    /// <summary>
    /// The errors grouped by field, parsed from the <c>"Field: message"</c> form the validation behaviour writes. An error
    /// without a field prefix is filed under the empty key. Every surface renders this one shape.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors => _fieldErrors ??= Group(Errors);

    private IReadOnlyDictionary<string, string[]>? _fieldErrors;

    private static Dictionary<string, string[]> Group(IReadOnlyList<string> errors)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var error in errors)
        {
            var separator = error.IndexOf(": ", StringComparison.Ordinal);
            var field = separator > 0 && !error.AsSpan(0, separator).Contains(' ') ? error[..separator] : "";
            var message = field.Length > 0 ? error[(separator + 2)..] : error;
            if (!groups.TryGetValue(field, out var list))
                groups[field] = list = [];
            list.Add(message);
        }

        return groups.ToDictionary(g => g.Key, g => g.Value.ToArray(), StringComparer.Ordinal);
    }
}

public sealed class UnauthorizedException()
    : ModulusException("Authentication required.");

public sealed class ForbiddenException(string permission, string? detail = null)
    : ModulusException(detail ?? $"Access denied. Required permission: {permission}")
{
    /// <summary>The permission the caller lacks.</summary>
    public string Permission { get; } = permission;
}

/// <summary>A permission that needs a recent multi-factor sign-in was used without one. Answered as <c>401</c> with code <c>STEP_UP_REQUIRED</c>.</summary>
public sealed class StepUpRequiredException(string permission)
    : ModulusException($"A recent multi-factor sign-in is required to use {permission}.")
{
    /// <summary>The permission that needs the sign-in.</summary>
    public string Permission { get; } = permission;
}

public sealed class FeatureDisabledException(string feature)
    : ModulusException($"Feature not available for this tenant: {feature}")
{
    public string Feature { get; } = feature;
}

public sealed class ConflictException(string message)
    : ModulusException(message);

/// <summary>
/// A secret store (for example OpenBao) could not be reached and no bounded stale value is available. Answered as
/// <c>503</c> with code <c>SERVICE_UNAVAILABLE</c>. The message never names the secret path.
/// </summary>
public sealed class SecretStoreUnavailableException(string message, Exception? inner = null)
    : ModulusException(message, inner);

/// <summary>
/// A secret the framework needs is missing from its store. A server-side fault (<c>500</c>): callers never learn which
/// secret was missing. The message never names the secret path.
/// </summary>
public sealed class SecretNotFoundException(string message)
    : ModulusException(message);

/// <summary>
/// A tenant's database cannot be resolved right now: no catalog row, a status that does not serve requests, a provider
/// mismatch, a tampered secret reference or a secret older than the catalog expects. Answered as <c>503</c>. The message
/// never names a connection string or a secret path.
/// </summary>
public sealed class TenantDatabaseUnavailableException(string message, Exception? inner = null)
    : ModulusException(message, inner);
