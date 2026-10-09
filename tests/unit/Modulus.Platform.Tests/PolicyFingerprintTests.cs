namespace Modulus.Platform.Tests;

using FluentAssertions;
using Modulus.Authorization.Audit;
using Modulus.Authorization.Resources;
using Xunit;

[Trait("Category", "Unit")]
public sealed class PolicyFingerprintTests
{
    private sealed class Invoice;

    private sealed class Registry(ResourcePolicy? policy) : IResourcePolicyRegistry
    {
        public ResourcePolicy? Find(Type resourceType) => policy;
    }

    private static ResourcePolicy Policy(string? version = null, string action = "approve")
        => ResourcePolicy.Define(p =>
        {
            if (version is not null)
                p.Version(version);
            p.Allow(action, _ => true).Deny("*", _ => false);
        });

    [Fact]
    public void The_same_shape_gives_the_same_fingerprint()
        => Policy("1").Fingerprint.Should().Be(Policy("1").Fingerprint);

    [Fact]
    public void A_changed_rule_or_version_changes_it()
    {
        Policy("1").Fingerprint.Should().NotBe(Policy("2").Fingerprint);
        Policy("1").Fingerprint.Should().NotBe(Policy("1", "reject").Fingerprint);
    }

    [Fact]
    public void A_decision_is_current_only_while_the_policy_is_unchanged()
    {
        var then = Policy("1");
        var decision = new AccessDecisionAuditEvent("Invoice", "approve", true, null, "u") { PolicyFingerprint = then.Fingerprint };

        PolicyFingerprints.IsCurrent(decision, new Registry(Policy("1")), typeof(Invoice)).Should().BeTrue();
        PolicyFingerprints.IsCurrent(decision, new Registry(Policy("2")), typeof(Invoice)).Should().BeFalse();
        PolicyFingerprints.IsCurrent(decision, new Registry(null), typeof(Invoice)).Should().BeFalse();
        PolicyFingerprints.IsCurrent(decision with { PolicyFingerprint = null }, new Registry(Policy("1")), typeof(Invoice)).Should().BeFalse();
    }
}
