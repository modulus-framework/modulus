namespace Modulus.Realtime;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

/// <summary>A client asking to follow a topic.</summary>
/// <param name="User">The caller.</param>
/// <param name="Topic">The topic, e.g. <c>orders:42</c>.</param>
/// <param name="Services">The request's services (tenant already resolved).</param>
public sealed record RealtimeTopicContext(ClaimsPrincipal User, string Topic, IServiceProvider Services)
{
    /// <summary>What follows the pattern's <c>*</c> (<c>42</c> for <c>orders:42</c> against <c>orders:*</c>); empty for an exact pattern.</summary>
    public string Key { get; init; } = string.Empty;
}

/// <summary>
/// A topic clients may follow: an exact name (<c>catalog:products</c>) or a prefix ending in <c>*</c> (<c>orders:*</c>).
/// Following it needs <see cref="Permission"/> (when set) and <see cref="Authorize"/> to return true (when set), for
/// resource checks such as "is this the caller's order".
/// </summary>
internal sealed record RealtimeTopicDefinition(string Pattern, string? Permission, Func<RealtimeTopicContext, ValueTask<bool>>? Authorize)
{
    internal bool IsPrefix => Pattern.EndsWith('*');

    internal bool TryMatch(string topic, out string key)
    {
        if (IsPrefix)
        {
            var prefix = Pattern[..^1];
            if (topic.Length > prefix.Length && topic.StartsWith(prefix, StringComparison.Ordinal))
            {
                key = topic[prefix.Length..];
                return true;
            }
        }
        else if (string.Equals(topic, Pattern, StringComparison.Ordinal))
        {
            key = string.Empty;
            return true;
        }

        key = string.Empty;
        return false;
    }
}

/// <summary>Why a topic subscription was refused.</summary>
public enum RealtimeTopicDecision
{
    /// <summary>The caller may follow it.</summary>
    Allowed,

    /// <summary>No registered topic matches the name.</summary>
    Unknown,

    /// <summary>A topic matches but the caller may not follow it.</summary>
    Denied,
}

/// <summary>Decides whether a caller may follow a topic (registered with <see cref="RealtimeBuilder.AddTopic"/>).</summary>
public interface IRealtimeTopicAuthorizer
{
    /// <summary>Checks <paramref name="topic"/> for <paramref name="user"/>; the most specific matching pattern decides.</summary>
    ValueTask<RealtimeTopicDecision> AuthorizeAsync(ClaimsPrincipal user, string topic, IServiceProvider services, CancellationToken ct = default);
}

internal sealed class RealtimeTopicAuthorizer(IEnumerable<RealtimeTopicDefinition> definitions) : IRealtimeTopicAuthorizer
{
    // Exact names first, then the longest prefix, so "orders:archive" can be stricter than "orders:*".
    private readonly RealtimeTopicDefinition[] _definitions = [.. definitions
        .OrderBy(d => d.IsPrefix)
        .ThenByDescending(d => d.Pattern.Length)];

    public async ValueTask<RealtimeTopicDecision> AuthorizeAsync(ClaimsPrincipal user, string topic, IServiceProvider services, CancellationToken ct = default)
    {
        if (!RealtimeTopics.IsValid(topic))
            return RealtimeTopicDecision.Unknown;

        foreach (var definition in _definitions)
        {
            if (!definition.TryMatch(topic, out var key))
                continue;

            if (definition.Permission is { } permission
                && !await RealtimePermissions.HasAsync(services, user, permission).ConfigureAwait(false))
            {
                return RealtimeTopicDecision.Denied;
            }

            if (definition.Authorize is { } authorize
                && !await authorize(new RealtimeTopicContext(user, topic, services) { Key = key }).ConfigureAwait(false))
            {
                return RealtimeTopicDecision.Denied;
            }

            return RealtimeTopicDecision.Allowed;
        }

        return RealtimeTopicDecision.Unknown;
    }
}

internal static class RealtimeTopics
{
    public const int MaxLength = 200;

    public static bool IsValid(string? topic)
        => !string.IsNullOrWhiteSpace(topic) && topic.Length <= MaxLength && !topic.Contains('*', StringComparison.Ordinal)
           && topic.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c != ',');

    public static void Validate(string topic)
    {
        if (!IsValid(topic))
            throw new ArgumentException($"'{topic}' is not a valid topic: 1-{MaxLength} characters without '*', ',' or whitespace.", nameof(topic));
    }

    public static void ValidatePattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var body = pattern.EndsWith('*') ? pattern[..^1] : pattern;
        if (body.Length == 0 || !IsValid(body))
            throw new ArgumentException($"'{pattern}' is not a valid topic pattern: a name, or a prefix ending in '*' (e.g. 'orders:*').", nameof(pattern));
    }
}

/// <summary>Permission checks through the <c>:</c> policies, with a <c>permission</c>-claim fallback when none is registered.</summary>
internal static class RealtimePermissions
{
    public static async Task<bool> HasAsync(IServiceProvider services, ClaimsPrincipal user, string permission)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        var policies = services.GetService<IAuthorizationPolicyProvider>();
        var policy = policies is null ? null : await policies.GetPolicyAsync(permission).ConfigureAwait(false);
        if (policy is null)
            return user.HasClaim("permission", permission);

        var authorization = services.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(user, policy).ConfigureAwait(false)).Succeeded;
    }
}
