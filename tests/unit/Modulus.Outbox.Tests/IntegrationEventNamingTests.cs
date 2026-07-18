using FluentAssertions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Xunit;

namespace Modulus.Outbox.Tests;

/// <summary>
/// The stable transport name is the wire/persistence identity of an integration
/// event. These lock in that it is derived from the attribute (or the
/// assembly-independent full name) — never the assembly-qualified name — and that
/// the registry round-trips it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IntegrationEventNamingTests
{
    [Fact]
    public void GetName_WithoutAttribute_UsesAssemblyIndependentFullName()
    {
        var name = IntegrationEventNaming.GetName(typeof(PlainEvent));

        name.Should().Be(typeof(PlainEvent).FullName);
        name.Should().NotContain(",", "the name must not carry assembly identity");
    }

    [Fact]
    public void GetName_WithAttribute_UsesTheStableName()
        => IntegrationEventNaming.GetName(typeof(NamedEvent))
            .Should().Be("catalog.product-created.v1");

    [Fact]
    public void Registry_RoundTrips_ByStableName()
    {
        var registry = new IntegrationEventRegistry();
        registry.Register(typeof(NamedEvent));
        registry.Register(typeof(PlainEvent));

        registry.TryGetType("catalog.product-created.v1", out var named).Should().BeTrue();
        named.Should().Be(typeof(NamedEvent));

        registry.TryGetType(typeof(PlainEvent).FullName!, out var plain).Should().BeTrue();
        plain.Should().Be(typeof(PlainEvent));

        registry.GetRoutingKeys().Should().Contain("catalog.product-created.v1");
    }

    private sealed record PlainEvent : IntegrationEventBase
    {
        public PlainEvent() : base("plain") { }
    }

    [IntegrationEventName("catalog.product-created.v1")]
    private sealed record NamedEvent : IntegrationEventBase
    {
        public NamedEvent() : base("catalog.product-created.v1") { }
    }
}
