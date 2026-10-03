namespace Modulus.Webhooks;

using System.Diagnostics.CodeAnalysis;

/// <summary>An integration event exposed as a webhook.</summary>
/// <param name="Name">The event's stable name (its <c>[IntegrationEventName]</c>), the body's <c>type</c>.</param>
/// <param name="EventType">The integration event's CLR type.</param>
/// <param name="Description">What it means, for the event-type listing.</param>
public sealed record WebhookEventDescriptor(string Name, Type EventType, string? Description)
{
    internal Func<object, object?>? Payload { get; init; }
}

/// <summary>The events subscriptions can choose from (registered with <see cref="WebhooksBuilder.AddEvent{TEvent}"/>).</summary>
public interface IWebhookEventCatalog
{
    /// <summary>The exposed events, by name.</summary>
    IReadOnlyList<WebhookEventDescriptor> Events { get; }

    /// <summary>Looks an event up by name.</summary>
    bool TryGet(string name, [NotNullWhen(true)] out WebhookEventDescriptor? descriptor);
}

internal sealed class WebhookEventCatalog(IEnumerable<WebhookEventDescriptor> descriptors) : IWebhookEventCatalog
{
    /// <summary>The event sent by the management API's test endpoint; every subscription receives it.</summary>
    public const string TestEventName = "webhook.test";

    private readonly Dictionary<string, WebhookEventDescriptor> _byName = descriptors
        .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WebhookEventDescriptor> Events
        => [.. _byName.Values.OrderBy(d => d.Name, StringComparer.Ordinal)];

    public bool TryGet(string name, [NotNullWhen(true)] out WebhookEventDescriptor? descriptor)
        => _byName.TryGetValue(name, out descriptor);
}
