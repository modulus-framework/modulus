using FluentAssertions;
using Modulus.AuditLogging.Security;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>The company audit summary (decision 7): six decoded fields, display names for actors, nothing else.</summary>
[Trait("Category", "Unit")]
public sealed class CompanyAuditSummaryTests
{
    private static readonly Guid Chain = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Alice = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bob = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static SecurityAuditRecord Record(string? actor, string? target = null) => new()
    {
        ChainId = Chain,
        Sequence = 1,
        OccurredAt = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero),
        Category = "identity",
        Action = "role.assigned",
        Outcome = "success",
        Actor = actor,
        Target = target,
        CorrelationId = "corr-1",
        Details = new Dictionary<string, string?> { ["email"] = "alice@example.test", ["password"] = "secret" },
        PreviousHash = "p",
        Hash = "h",
    };

    [Fact]
    public void A_row_has_the_six_decoded_fields_and_no_details_hashes_or_correlation()
    {
        var rows = CompanyAuditSummaryEndpoints.Summarize([Record(Alice.ToString("D"), "role:Admin")], new Dictionary<Guid, string>());

        var row = rows.Should().ContainSingle().Subject;
        row.Should().BeEquivalentTo(new CompanyAuditSummaryRow(
            new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero), "identity", "role.assigned", "success", CompanyAuditSummaryEndpoints.UnknownUser, "role:Admin"));
        typeof(CompanyAuditSummaryRow).GetProperties().Select(p => p.Name).Should()
            .BeEquivalentTo(["OccurredAt", "Category", "Action", "Outcome", "ActorName", "Target"]);
    }

    [Fact]
    public void An_actor_with_a_display_name_is_shown_by_that_name()
    {
        var rows = CompanyAuditSummaryEndpoints.Summarize(
            [Record(Alice.ToString("D"))],
            new Dictionary<Guid, string> { [Alice] = "Alice Example" });

        rows.Single().ActorName.Should().Be("Alice Example");
    }

    [Fact]
    public void An_actor_that_cannot_be_resolved_is_shown_as_unknown_user_and_the_page_still_returns()
    {
        var rows = CompanyAuditSummaryEndpoints.Summarize(
            [Record(Bob.ToString("D")), Record(Alice.ToString("D"))],
            new Dictionary<Guid, string> { [Alice] = "Alice Example" });

        rows.Select(r => r.ActorName).Should().Equal(CompanyAuditSummaryEndpoints.UnknownUser, "Alice Example");
    }

    [Fact]
    public void A_missing_actor_is_shown_as_system_and_a_client_id_as_it_is()
    {
        var rows = CompanyAuditSummaryEndpoints.Summarize([Record(null), Record("integration-client")], new Dictionary<Guid, string>());

        rows.Select(r => r.ActorName).Should().Equal("system", "integration-client");
    }

    [Fact]
    public void Only_account_ids_are_looked_up_and_each_once()
    {
        var ids = CompanyAuditSummaryEndpoints.AccountIds(
            [Record(Alice.ToString("D")), Record(Alice.ToString("D")), Record(Bob.ToString("D")), Record("integration-client"), Record(null)]);

        ids.Should().BeEquivalentTo([Alice, Bob]);
    }
}
