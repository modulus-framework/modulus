namespace Modulus.Webhooks;

/// <summary>The event-name patterns a subscription lists: an exact name, <c>prefix.*</c> or <c>*</c>.</summary>
public static class WebhookEventFilter
{
    /// <summary>Every event.</summary>
    public const string All = "*";

    /// <summary>Whether <paramref name="eventType"/> matches any of <paramref name="patterns"/> (names compare ordinally, ignoring case).</summary>
    public static bool Matches(IEnumerable<string> patterns, string eventType)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(eventType);
        foreach (var pattern in patterns)
        {
            if (pattern == All)
                return true;
            if (pattern.EndsWith(".*", StringComparison.Ordinal)
                ? eventType.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(pattern, eventType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Whether <paramref name="pattern"/> is <c>*</c>, a known event name, or a prefix (<c>catalog.*</c>) of one.</summary>
    public static bool IsValid(string pattern, IEnumerable<string> knownEvents)
    {
        ArgumentNullException.ThrowIfNull(knownEvents);
        if (string.IsNullOrWhiteSpace(pattern))
            return false;
        if (pattern == All)
            return true;
        return knownEvents.Any(e => Matches([pattern], e));
    }
}
