using FluentAssertions;
using Modulus.Authorization.Governance;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the recertification campaign turns effective-access snapshots into reviewable
/// lines, tracks certify/revoke decisions, reports the revoked lines as its actionable
/// output, and completes only when nothing is left pending (blueprint §5.14, §16).
/// </summary>
[Trait("Category", "Unit")]
public sealed class RecertificationCampaignTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Manager = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static EffectiveAccessReport ReportFor(Guid user)
        => new(
            user,
            DirectPermissions: new HashSet<string>(["orders:read", "orders:edit"], StringComparer.OrdinalIgnoreCase),
            DelegatedPermissions: [new DelegatedPermission("orders:approve", Manager, Guid.NewGuid())],
            AllPermissions: new HashSet<string>(["orders:read", "orders:edit", "orders:approve"], StringComparer.OrdinalIgnoreCase),
            SodViolations: []);

    [Fact]
    public void Campaign_ExpandsSnapshotsIntoReviewableLines()
    {
        var campaign = new RecertificationCampaign("2026-Q3", [ReportFor(Alice)]);

        campaign.Items.Should().HaveCount(3);
        campaign.Items.Should().Contain(i => i.Permission == "orders:approve" && i.Source == AccessSource.Delegated);
        campaign.Items.Should().Contain(i => i.Permission == "orders:read" && i.Source == AccessSource.Direct);
    }

    [Fact]
    public void NewCampaign_IsAllPending_AndNotComplete()
    {
        var campaign = new RecertificationCampaign("2026-Q3", [ReportFor(Alice)]);

        campaign.IsComplete.Should().BeFalse();
        campaign.Pending.Should().HaveCount(3);
    }

    [Fact]
    public void CertifyingAndRevokingEveryLine_CompletesTheCampaign_AndSurfacesRevocations()
    {
        var campaign = new RecertificationCampaign("2026-Q3", [ReportFor(Alice)]);

        campaign.Certify(Alice, "orders:read");
        campaign.Certify(Alice, "orders:edit");
        campaign.Revoke(Alice, "orders:approve"); // reviewer decides the delegated approval is no longer needed

        campaign.IsComplete.Should().BeTrue();
        campaign.Pending.Should().BeEmpty();
        campaign.Revoked.Should().ContainSingle()
            .Which.Permission.Should().Be("orders:approve");
    }

    [Fact]
    public void DecisionsAreCaseInsensitive_OnPermissionName()
    {
        var campaign = new RecertificationCampaign("2026-Q3", [ReportFor(Alice)]);

        campaign.Certify(Alice, "ORDERS:READ");

        campaign.Items.Single(i => i.Permission == "orders:read").Decision
            .Should().Be(RecertificationDecision.Certified);
    }
}
