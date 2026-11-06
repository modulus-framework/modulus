namespace Modulus.Analytics.Tests;

using FluentAssertions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AnalyticsRecordIdsTests
{
    [Fact]
    public void The_same_scope_and_key_always_give_the_same_id()
        => AnalyticsRecordIds.Deterministic("orders", "42").Should().Be(AnalyticsRecordIds.Deterministic("orders", "42"));

    [Fact]
    public void Different_keys_and_different_scopes_give_different_ids()
    {
        AnalyticsRecordIds.Deterministic("orders", "42").Should().NotBe(AnalyticsRecordIds.Deterministic("orders", "43"));
        AnalyticsRecordIds.Deterministic("orders", "42").Should().NotBe(AnalyticsRecordIds.Deterministic("invoices", "42"));
    }

    [Fact]
    public void The_scope_and_key_boundary_cannot_be_forged()
        => AnalyticsRecordIds.Deterministic("a", "b|c").Should().NotBe(AnalyticsRecordIds.Deterministic("a|b", "c"));

    [Fact]
    public void The_id_is_a_version_8_variant_1_uuid_and_never_empty()
    {
        var id = AnalyticsRecordIds.Deterministic("orders", "1");

        id.Should().NotBe(Guid.Empty);
        id.ToString("D")[14].Should().Be('8', "the version nibble marks a custom, hash-derived UUID");
        "89ab".Should().Contain(id.ToString("D")[19].ToString(), "the variant bits are 10");
    }

    [Fact]
    public void An_empty_scope_is_refused()
    {
        var act = () => AnalyticsRecordIds.Deterministic("", "1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Known_input_gives_a_pinned_id_so_it_never_changes_between_releases()
        => AnalyticsRecordIds.Deterministic("orders", "1").ToString("D").Should().Be("b6e63488-f743-8524-bd68-1a3a8e1ba9ba");
}
