using FluentAssertions;
using Xunit;

namespace Modulus.Messaging.Tests;

/// <summary>NFR-10: a type name from the wire is deserialized only when it names a registered message type.</summary>
[Trait("Category", "Unit")]
public sealed class MessageTypeAllowListTests
{
    public interface IOrderEvent;

    public sealed record OrderSubmitted(Guid OrderId) : IOrderEvent;

    public sealed record Unregistered(string Text);

    private static MessageTypeAllowList AllowListFor(params Type[] registered) =>
        new(() => registered);

    private static string NameOf(Type type) => type.AssemblyQualifiedName!;

    [Fact]
    public void A_registered_type_resolves_to_itself()
    {
        var list = AllowListFor(typeof(OrderSubmitted));

        list.Resolve(NameOf(typeof(OrderSubmitted))).Should().Be(typeof(OrderSubmitted));
    }

    [Fact]
    public void A_concrete_type_published_to_a_base_or_interface_consumer_resolves()
    {
        // A consumer of IOrderEvent receives the concrete OrderSubmitted, so its name must be allowed (FR-01).
        var list = AllowListFor(typeof(IOrderEvent));

        list.Resolve(NameOf(typeof(OrderSubmitted))).Should().Be(typeof(OrderSubmitted));
    }

    [Fact]
    public void A_loadable_type_that_is_not_registered_is_refused_and_never_resolved_by_name()
    {
        // Unregistered is a real type in this assembly, so Type.GetType would resolve it. The allow-list must not.
        var list = AllowListFor(typeof(OrderSubmitted));

        Type.GetType(NameOf(typeof(Unregistered))).Should().NotBeNull("the name is real, so only the allow-list can refuse it");
        list.Resolve(NameOf(typeof(Unregistered))).Should().BeNull();
    }

    [Fact]
    public void A_framework_type_that_is_not_a_message_is_refused()
    {
        var list = AllowListFor(typeof(OrderSubmitted));

        list.Resolve(NameOf(typeof(System.Text.StringBuilder))).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not.A.Real.Type, Nowhere")]
    public void Missing_or_unknown_names_are_refused(string? name)
    {
        var list = AllowListFor(typeof(OrderSubmitted));

        list.Resolve(name).Should().BeNull();
    }

    [Fact]
    public void Without_an_allow_list_a_standalone_transport_keeps_the_framework_resolution()
    {
        // A transport used without a bus has no registered types, so it keeps the previous name resolution.
        MessageTypeAllowList.ResolveWireType(NameOf(typeof(OrderSubmitted)), allowList: null).Should().Be(typeof(OrderSubmitted));
    }

    [Fact]
    public void With_an_allow_list_the_bus_resolution_refuses_unregistered_names()
    {
        var list = AllowListFor(typeof(OrderSubmitted));

        MessageTypeAllowList.ResolveWireType(NameOf(typeof(Unregistered)), list).Should().BeNull();
    }
}
