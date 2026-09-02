using FluentAssertions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Xunit;

namespace Modulus.Outbox.Tests;

[Trait("Category", "Unit")]
public sealed class MessageSerializerRoundTripTests
{
    /// <summary>
    /// Validates the B2 fix: SystemTextJsonMessageSerializer uses
    /// PropertyNameCaseInsensitive + CamelCase, so payloads serialized with
    /// camelCase property names deserialise correctly regardless of the
    /// original property casing. This is the root cause of the B2 blocker —
    /// consumers using raw JsonSerializer (case-sensitive) dropped properties
    /// when the payload used camelCase keys.
    /// </summary>
    [Fact]
    public void RoundTrip_CamelCasePayload_DeserialisesWithMatchingValues()
    {
        IMessageSerializer serializer = new SystemTextJsonMessageSerializer();

        var original = new TestPayload { Name = "hello", Count = 42 };
        var json = serializer.Serialize(original, typeof(TestPayload));

        // Payload should be camelCase (the serializer's policy)
        json.Should().Contain("\"name\"").And.Contain("\"count\"");

        var deserialized = (TestPayload)serializer.Deserialize(json, typeof(TestPayload))!;
        deserialized.Should().NotBeNull();
        deserialized.Name.Should().Be("hello");
        deserialized.Count.Should().Be(42);
    }

    [Fact]
    public void RoundTrip_PascalCaseInput_DeserialisesCorrectly()
    {
        IMessageSerializer serializer = new SystemTextJsonMessageSerializer();

        // Simulate a payload written with PascalCase keys (legacy format)
        var json = """{"Name":"world","Count":7}""";
        var deserialized = (TestPayload)serializer.Deserialize(json, typeof(TestPayload))!;

        deserialized.Should().NotBeNull();
        deserialized.Name.Should().Be("world");
        deserialized.Count.Should().Be(7);
    }

    [Fact]
    public void RoundTrip_NullValues_HandledGracefully()
    {
        IMessageSerializer serializer = new SystemTextJsonMessageSerializer();

        var json = """{"name":null,"count":0}""";
        var deserialized = (TestPayload)serializer.Deserialize(json, typeof(TestPayload))!;

        deserialized.Should().NotBeNull();
        deserialized.Name.Should().BeNull();
        deserialized.Count.Should().Be(0);
    }

    public sealed class TestPayload
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }
}
