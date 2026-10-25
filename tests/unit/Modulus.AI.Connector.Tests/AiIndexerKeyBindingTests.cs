using FluentAssertions;
using Modulus.AI.Connector;

using Xunit;

namespace Modulus.AI.Connector.Tests;

/// <summary>An indexing key reads only the instance it is bound to.</summary>
[Trait("Category", "Unit")]
public sealed class AiIndexerKeyBindingTests
{
    [Fact]
    public void A_bound_key_reads_its_own_instance()
    {
        var instance = new AiAppInstanceOptions { AppInstanceId = "a", ApiKeyHash = AiApiKeys.Hash("key-a") };

        AiIndexer.KeyBelongsTo("ApiKey key-a", instance, instanceCount: 2).Should().BeTrue();
    }

    [Fact]
    public void Another_instances_key_is_refused()
    {
        var instance = new AiAppInstanceOptions { AppInstanceId = "a", ApiKeyHash = AiApiKeys.Hash("key-a") };

        AiIndexer.KeyBelongsTo("ApiKey key-b", instance, instanceCount: 2).Should().BeFalse();
        AiIndexer.KeyBelongsTo(null, instance, instanceCount: 2).Should().BeFalse();
    }

    [Fact]
    public void An_unbound_instance_is_served_only_when_it_is_the_single_instance()
    {
        var instance = new AiAppInstanceOptions { AppInstanceId = "solo" };

        AiIndexer.KeyBelongsTo("ApiKey anything", instance, instanceCount: 1).Should().BeTrue("the single-company case keeps working");
        AiIndexer.KeyBelongsTo("ApiKey anything", instance, instanceCount: 2).Should().BeFalse("with several instances a binding is required");
    }
}
