using FluentAssertions;
using Modulus.Events.Abstractions;
using Modulus.Testing.Architecture;
using Xunit;

namespace Modulus.Testing.Tests;

[Trait("Category", "Unit")]
public sealed class IntegrationEventNameRulesTests
{
    [IntegrationEventName("rules", "first-event")]
    private sealed record First(Guid Id) : IntegrationEventBase;

    [IntegrationEventName("rules", "duplicate-event")]
    private sealed record DuplicateA(Guid Id) : IntegrationEventBase;

    [IntegrationEventName("rules", "duplicate-event")]
    private sealed record DuplicateB(Guid Id) : IntegrationEventBase;

    [IntegrationEventName("Bad Name")]
    private sealed record Malformed(Guid Id) : IntegrationEventBase;

    [Fact]
    public void Duplicate_names_are_found()
    {
        var duplicates = ModuleBoundaryRules.FindDuplicateIntegrationEventNames();

        duplicates.Select(d => d.Name).Should().Contain("rules.duplicate-event.v1");
        var found = duplicates.Single(d => d.Name == "rules.duplicate-event.v1");
        found.Types.Should().BeEquivalentTo([typeof(DuplicateA), typeof(DuplicateB)]);
    }

    [Fact]
    public void Malformed_names_are_found_and_well_formed_ones_are_not()
    {
        var malformed = ModuleBoundaryRules.FindMalformedIntegrationEventNames();

        malformed.Should().Contain(m => m.Type == typeof(Malformed));
        malformed.Should().NotContain(m => m.Type == typeof(First));
    }
}
