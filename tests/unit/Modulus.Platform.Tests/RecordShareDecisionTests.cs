using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Sharing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using NSubstitute;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A8-1 decision path: a live share gives its one read action to the grantee where no rule granted it. It never overrides an explicit
/// deny, another company, or an action other than the shared one, and a revoked or expired share grants nothing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RecordShareDecisionTests
{
    private static readonly Guid s_company = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_grantee = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_recordId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly ResourcePolicy Policy = ResourcePolicy.Define(p => p
        .Deny("view", r => r.InState("Locked"))
        .Allow("view", r => r.OwnedByCaller())
        .Allow("edit", r => r.OwnedByCaller()));

    private sealed class Doc : IHasOwner, IHasWorkflowState
    {
        public Guid Id { get; init; }
        public Guid OwnerId { get; init; }
        public string WorkflowState { get; init; } = string.Empty;
    }

    private sealed class Registry(ResourcePolicy policy) : IResourcePolicyRegistry
    {
        public ResourcePolicy? Find(Type resourceType) => resourceType == typeof(Doc) ? policy : null;

        public IReadOnlyList<ResourcePolicy> All => [policy];
    }

    private sealed class Scope : ICurrentDataScope
    {
        public bool IsUnrestricted => true;
        public IReadOnlyCollection<Guid> OrgUnitIds { get; } = [];
    }

    private sealed class User(Guid? userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => userId is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness(Guid? caller, string state = "Draft")
    {
        public MovableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
        public ICurrentTenant Tenant { get; } = Substitute.For<ICurrentTenant>();
        public RecordShares Shares { get; private set; } = null!;
        public ResourceAuthorizer Authorizer { get; private set; } = null!;
        public Doc Record { get; } = new() { Id = s_recordId, OwnerId = s_owner, WorkflowState = state };

        public Harness Build()
        {
            Tenant.TenantId.Returns(s_company);
            Shares = new RecordShares(new InMemoryRecordShareStore(), Tenant, Clock, Options.Create(new RecordShareOptions()));
            Authorizer = new ResourceAuthorizer(new User(caller), new Scope(), new Registry(Policy), tenant: Tenant, shares: Shares);
            return this;
        }
    }

    private static readonly Guid s_stranger = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task A_live_share_gives_the_grantee_the_shared_read_action()
    {
        var h = new Harness(s_grantee).Build();
        await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));

        var decision = await h.Authorizer.AuthorizeAsync(h.Record, "view");

        decision.IsAllowed.Should().BeTrue();
        decision.Code.Should().Be(AccessReasonCodes.Shared);
    }

    [Fact]
    public async Task Without_a_share_the_grantee_is_still_refused()
    {
        var h = new Harness(s_grantee).Build();

        (await h.Authorizer.AuthorizeAsync(h.Record, "view")).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task A_share_never_grants_an_action_other_than_the_shared_read()
    {
        var h = new Harness(s_grantee).Build();
        await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));

        (await h.Authorizer.AuthorizeAsync(h.Record, "edit")).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task An_explicit_deny_still_stands_under_a_share()
    {
        var h = new Harness(s_grantee, state: "Locked").Build();
        await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));

        var decision = await h.Authorizer.AuthorizeAsync(h.Record, "view");

        decision.IsAllowed.Should().BeFalse("deny wins over a share (ADR-03)");
        decision.Code.Should().Be(AccessReasonCodes.PolicyViolation, "a deny rule keeps its ordinary code");
    }

    [Fact]
    public async Task A_share_from_another_company_grants_nothing()
    {
        var h = new Harness(s_grantee).Build();
        await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));
        h.Tenant.TenantId.Returns(Guid.Parse("55555555-5555-5555-5555-555555555555"));

        (await h.Authorizer.AuthorizeAsync(h.Record, "view")).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task A_revoked_or_expired_share_grants_nothing()
    {
        var h = new Harness(s_grantee).Build();
        var share = await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));

        await h.Shares.RevokeAsync(share.Id, s_owner);
        (await h.Authorizer.AuthorizeAsync(h.Record, "view")).IsAllowed.Should().BeFalse("a revoked share is over");

        var again = await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));
        h.Clock.Now = h.Clock.Now.AddHours(2);
        (await h.Authorizer.AuthorizeAsync(h.Record, "view")).IsAllowed.Should().BeFalse("an expired share is over");
        again.Should().NotBeNull();
    }

    [Fact]
    public async Task Explain_shows_a_share_as_the_reason_an_action_is_allowed()
    {
        var h = new Harness(s_grantee).Build();
        (await h.Authorizer.ExplainAsync(h.Record, "view")).Decision.IsAllowed.Should().BeFalse();

        await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));

        var explained = await h.Authorizer.ExplainAsync(h.Record, "view");
        explained.Decision.IsAllowed.Should().BeTrue();
        explained.Decision.Code.Should().Be(AccessReasonCodes.Shared);
        explained.Rules.Should().NotBeEmpty("the rules are still listed for diagnosis");
    }

    [Fact]
    public async Task Explain_does_not_show_a_share_over_an_explicit_deny()
    {
        var h = new Harness(s_grantee, state: "Locked").Build();
        await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));

        (await h.Authorizer.ExplainAsync(h.Record, "view")).Decision.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task A_share_to_someone_else_does_not_reach_a_stranger()
    {
        var h = new Harness(s_stranger).Build();
        await h.Shares.CreateAsync(nameof(Doc), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));

        (await h.Authorizer.AuthorizeAsync(h.Record, "view")).IsAllowed.Should().BeFalse();
    }
}
