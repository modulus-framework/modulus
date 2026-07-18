using FluentAssertions;
using Modulus.Authorization.Resources;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Proves the <see cref="ResourceAuthorizer"/> edge bridge builds the policy request
/// from the current principal's identity + data scope and is fail-closed: no policy or
/// no granting rule denies, and the in-scope probe reuses <see cref="ICurrentDataScope"/>
/// so the single-item check mirrors the bulk list filter.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ResourceAuthorizerTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Unit = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    private static readonly ResourcePolicy DocPolicy = ResourcePolicy.Define(p => p
        .Allow("edit", r => r.OwnedByCaller() && r.InState("Draft"))
        .Allow("read", r => r.InCallerScope()));

    [Fact]
    public void NoPolicyRegistered_Denies_FailClosed()
    {
        var authorizer = new ResourceAuthorizer(
            new StubUser(Owner), Unrestricted, new StubRegistry(policy: null));

        var decision = authorizer.Authorize(new Doc(), "edit");

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("no resource policy");
    }

    [Fact]
    public void OwnerInDraft_MayEdit_ViaCurrentUserIdentity()
    {
        var authorizer = new ResourceAuthorizer(
            new StubUser(Owner), Unrestricted, new StubRegistry(DocPolicy));

        authorizer.Authorize(new Doc { OwnerId = Owner, WorkflowState = "Draft" }, "edit")
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void NonOwner_MayNotEdit()
    {
        var authorizer = new ResourceAuthorizer(
            new StubUser(Guid.NewGuid()), Unrestricted, new StubRegistry(DocPolicy));

        authorizer.Authorize(new Doc { OwnerId = Owner, WorkflowState = "Draft" }, "edit")
            .IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void AnonymousPrincipal_IsDenied()
    {
        var authorizer = new ResourceAuthorizer(
            new StubUser(userId: null), Unrestricted, new StubRegistry(DocPolicy));

        authorizer.Authorize(new Doc { OwnerId = Owner, WorkflowState = "Draft" }, "edit")
            .IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void ReadIsScopeGated_ReusingCurrentDataScope()
    {
        var doc = new Doc { OrgUnitId = Unit, WorkflowState = "Draft" };

        // In scope → allowed.
        new ResourceAuthorizer(new StubUser(Owner), new StubScope(Unit), new StubRegistry(DocPolicy))
            .Authorize(doc, "read").IsAllowed.Should().BeTrue();

        // Out of scope (restricted to a different unit) → denied, mirroring the list filter.
        new ResourceAuthorizer(new StubUser(Owner), new StubScope(Guid.NewGuid()), new StubRegistry(DocPolicy))
            .Authorize(doc, "read").IsAllowed.Should().BeFalse();
    }

    private static StubScope Unrestricted => new(unrestricted: true);

    private sealed class Doc : Modulus.Core.Abstractions.Entities.IHasOwner,
        Modulus.Core.Abstractions.Entities.IHasOrgUnit,
        Modulus.Core.Abstractions.Entities.IHasWorkflowState
    {
        public Guid OwnerId { get; init; }
        public Guid OrgUnitId { get; init; }
        public string WorkflowState { get; init; } = string.Empty;
    }

    private sealed class StubRegistry(ResourcePolicy? policy) : IResourcePolicyRegistry
    {
        public ResourcePolicy? Find(Type resourceType) => policy;
    }

    private sealed class StubScope : ICurrentDataScope
    {
        public StubScope(bool unrestricted) { IsUnrestricted = unrestricted; OrgUnitIds = []; }
        public StubScope(params Guid[] units) { OrgUnitIds = units; }
        public bool IsUnrestricted { get; }
        public IReadOnlyCollection<Guid> OrgUnitIds { get; }
    }

    private sealed class StubUser(Guid? userId, params string[] permissions) : ICurrentUser
    {
        private readonly HashSet<string> _permissions = new(permissions, StringComparer.OrdinalIgnoreCase);

        public Guid? UserId => userId;
        public string? UserName => userId?.ToString();
        public string? Email => null;
        public bool IsAuthenticated => userId is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => _permissions.Contains(permission);
        public IReadOnlyList<string> Permissions => [.. _permissions];
    }
}
