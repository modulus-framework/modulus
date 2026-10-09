using FluentAssertions;
using Modulus.Authorization.Approval;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>"Up to this amount": approval limits as data, and the rules that keep one person from holding two steps.</summary>
[Trait("Category", "Unit")]
public sealed class ApprovalAuthorityTests
{
    private const string Approve = "procurement:purchase-order:approve";
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Merch = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid MerchTeam = Guid.Parse("11111111-0000-0000-0000-00000000000a");
    private static readonly Guid Finance = Guid.Parse("22222222-0000-0000-0000-000000000002");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Principal(PrincipalGrantQuery query) : IPrincipalGrantQuerySource
    {
        public PrincipalGrantQuery Current => query;
    }

    private sealed class PurchaseOrder : IHasApprovalAmount, IHasOrgUnit, IHasOwner, IHasApprovalTrail, IHasWorkflowState
    {
        public decimal ApprovalAmount { get; init; }
        public string? ApprovalCurrency { get; init; }
        public Guid OrgUnitId { get; init; }
        public Guid OwnerId { get; init; }
        public IReadOnlyCollection<Guid> ActedByUserIds { get; init; } = [];
        public string WorkflowState { get; init; } = "Submitted";
    }

    private static ApprovalAuthorityEvaluator Evaluator(
        InMemoryApprovalAuthorityStore store, PrincipalGrantQuery who, IOrgHierarchy? hierarchy = null, IDelegationStore? delegations = null)
        => new(store, new Principal(who), hierarchy, delegations, new Clock(Now));

    private static PrincipalGrantQuery AsManager(Guid id) => new(id, ["Manager"]);

    private static ApprovalAuthority RoleLimit(string role, decimal max, string? currency = "USD", string? type = null, Guid? unit = null,
        DateTimeOffset? from = null, DateTimeOffset? until = null)
        => new(GrantHolderType.Role, role, Approve, max, currency, type, unit, from, until);

    [Fact]
    public void An_amount_up_to_the_limit_is_covered_and_one_above_it_is_not()
    {
        var store = new InMemoryApprovalAuthorityStore().Add(RoleLimit("Manager", 10_000));
        var evaluator = Evaluator(store, AsManager(Alice));

        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 10_000, ApprovalCurrency = "USD" })
            .Should().Be(new ApprovalCheck(true, AccessReasonCodes.Allowed, 10_000));
        var over = evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 10_000.01m, ApprovalCurrency = "USD" });
        over.IsWithinAuthority.Should().BeFalse();
        over.ReasonCode.Should().Be(AccessReasonCodes.ApprovalLimitExceeded);
    }

    [Fact]
    public void Without_any_limit_nobody_has_authority_and_neither_does_a_document_without_an_amount()
    {
        var evaluator = Evaluator(new InMemoryApprovalAuthorityStore(), AsManager(Alice));

        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 1, ApprovalCurrency = "USD" }).ReasonCode
            .Should().Be(AccessReasonCodes.NoApprovalAuthority);
        evaluator.Check(Approve, new object()).ReasonCode.Should().Be(AccessReasonCodes.MetadataMissing);
    }

    [Fact]
    public void A_limit_is_in_one_currency_and_does_not_cover_another()
    {
        var store = new InMemoryApprovalAuthorityStore().Add(RoleLimit("Manager", 1_000_000, currency: "BDT"));
        var evaluator = Evaluator(store, AsManager(Alice));

        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 5, ApprovalCurrency = "USD" }).IsWithinAuthority.Should().BeFalse();
        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 5, ApprovalCurrency = "bdt" }).IsWithinAuthority.Should().BeTrue();
    }

    [Fact]
    public void The_largest_applicable_limit_counts_and_one_for_another_document_type_does_not_apply()
    {
        var store = new InMemoryApprovalAuthorityStore()
            .Add(RoleLimit("Manager", 5_000))
            .Add(new ApprovalAuthority(GrantHolderType.User, Alice.ToString(), Approve, 20_000, "USD"))
            .Add(RoleLimit("Manager", 999_999, type: "PaymentVoucher"));
        var evaluator = Evaluator(store, AsManager(Alice));
        var order = new PurchaseOrder { ApprovalAmount = 15_000, ApprovalCurrency = "USD" };

        evaluator.Check(Approve, order).Should().Be(new ApprovalCheck(true, AccessReasonCodes.Allowed, 20_000));
        Evaluator(store, AsManager(Bob)).Check(Approve, order).ReasonCode.Should().Be(AccessReasonCodes.ApprovalLimitExceeded);
    }

    [Fact]
    public void An_org_unit_limit_covers_that_unit_and_its_descendants_only()
    {
        var hierarchy = new InMemoryOrgHierarchy().AddUnit(Merch).AddUnit(MerchTeam, Merch).AddUnit(Finance);
        var store = new InMemoryApprovalAuthorityStore().Add(RoleLimit("Manager", 10_000, unit: Merch));
        var evaluator = Evaluator(store, AsManager(Alice), hierarchy);

        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 1, ApprovalCurrency = "USD", OrgUnitId = Merch }).IsWithinAuthority.Should().BeTrue();
        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 1, ApprovalCurrency = "USD", OrgUnitId = MerchTeam }).IsWithinAuthority.Should().BeTrue();
        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 1, ApprovalCurrency = "USD", OrgUnitId = Finance }).IsWithinAuthority.Should().BeFalse();
    }

    [Fact]
    public void A_limit_stops_applying_when_it_expires()
    {
        var store = new InMemoryApprovalAuthorityStore()
            .Add(RoleLimit("Manager", 10_000, until: Now))
            .Add(RoleLimit("Manager", 50_000, from: Now.AddDays(1)));

        Evaluator(store, AsManager(Alice)).Check(Approve, new PurchaseOrder { ApprovalAmount = 1, ApprovalCurrency = "USD" })
            .ReasonCode.Should().Be(AccessReasonCodes.NoApprovalAuthority);
    }

    [Fact]
    public void A_delegate_may_use_the_delegators_limit_never_more()
    {
        var store = new InMemoryApprovalAuthorityStore().Add(RoleLimit("Director", 100_000));
        var delegations = new InMemoryDelegationStore();
        delegations.Delegate(Alice, ["Director"], Bob, [Approve], Now.AddHours(-1), Now.AddHours(1));
        var bob = new PrincipalGrantQuery(Bob, []);
        var evaluator = Evaluator(store, bob, delegations: delegations);

        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 100_000, ApprovalCurrency = "USD" }).IsWithinAuthority.Should().BeTrue();
        evaluator.Check(Approve, new PurchaseOrder { ApprovalAmount = 100_001, ApprovalCurrency = "USD" }).IsWithinAuthority.Should().BeFalse();
        Evaluator(store, bob).Check(Approve, new PurchaseOrder { ApprovalAmount = 1, ApprovalCurrency = "USD" }).IsWithinAuthority
            .Should().BeFalse("without the delegation Bob holds no limit");
    }

    private static ResourcePolicy Policy() => ResourcePolicy.Define(p => p
        .Allow("approve", r => r.InState("Submitted")
                                && r.CallerHasPermission(Approve)
                                && r.NotOwnedByCaller()
                                && r.NotActedOnByCaller()
                                && r.WithinApprovalAuthority(Approve)));

    private static AccessDecision Decide(PurchaseOrder order, Guid caller, bool withAuthority = true)
        => Policy().Evaluate(new ResourceRequest(
            caller, _ => true, _ => true, ResourceAttributes.From(order), "approve",
            withinAuthority: withAuthority ? _ => true : null,
            priorActors: order.ActedByUserIds));

    [Fact]
    public void The_requester_and_anyone_who_already_acted_cannot_approve()
    {
        Decide(new PurchaseOrder { OwnerId = Alice }, Bob).IsAllowed.Should().BeTrue();
        Decide(new PurchaseOrder { OwnerId = Alice }, Alice).IsAllowed.Should().BeFalse("the requester cannot approve their own document");
        Decide(new PurchaseOrder { OwnerId = Alice, ActedByUserIds = [Bob] }, Bob).IsAllowed.Should().BeFalse("Bob already verified it");
    }

    [Fact]
    public void Without_an_authority_check_wired_approval_is_refused()
    {
        Decide(new PurchaseOrder { OwnerId = Alice }, Bob, withAuthority: false).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void An_anonymous_caller_is_never_a_valid_approver()
    {
        var request = new ResourceRequest(null, _ => true, _ => true, new ResourceAttributes(null, null, "Submitted"), "approve");

        request.NotOwnedByCaller().Should().BeFalse();
        request.NotActedOnByCaller().Should().BeFalse();
    }
}
