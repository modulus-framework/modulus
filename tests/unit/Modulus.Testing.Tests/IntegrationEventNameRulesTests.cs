using FluentAssertions;
using Modulus.Events.Abstractions;
using Modulus.Testing.Architecture;
using Xunit;

namespace Modulus.Testing.Tests;

[Trait("Category", "Unit")]
public sealed class IntegrationEventNameRulesTests
{
    private sealed class RulesArea;

    [IntegrationEvent<RulesArea>]
    private sealed record FirstEvent(Guid Id) : IntegrationEventBase;

    [IntegrationEventName("rules.duplicate-event.v1")]
    private sealed record DuplicateA(Guid Id) : IntegrationEventBase;

    [IntegrationEventName("rules.duplicate-event.v1")]
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
        malformed.Should().NotContain(m => m.Type == typeof(FirstEvent));
    }

    [Fact]
    public void Contract_file_reports_new_and_removed_names_and_round_trips()
    {
        var file = Path.Combine(Path.GetTempPath(), $"contract-{Guid.NewGuid():N}.txt");
        try
        {
            ModuleBoundaryRules.FindIntegrationEventContractChanges(file)
                .Should().Contain("NEW, not in the contract file: rules.first.v1");

            ModuleBoundaryRules.WriteIntegrationEventContract(file);
            ModuleBoundaryRules.FindIntegrationEventContractChanges(file).Should().BeEmpty();

            File.AppendAllText(file, "rules.renamed-away.v1" + Environment.NewLine);
            ModuleBoundaryRules.FindIntegrationEventContractChanges(file)
                .Should().ContainSingle().Which.Should().Be("REMOVED or renamed: rules.renamed-away.v1");
        }
        finally
        {
            File.Delete(file);
        }
    }
}
