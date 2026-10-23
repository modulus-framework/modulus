using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Fields;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Sharing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using NSubstitute;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A8-1 masked references: a share lets its grantee read the shared record, but a share grants no field permission, so the record's
/// classified fields stay masked for the grantee unless the grantee holds the field's own permission.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RecordShareFieldMaskTests
{
    private static readonly Guid s_company = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_grantee = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_recordId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly FieldSecurityProfile Profile = FieldSecurityProfile.Define(p => p
        .Classification(FieldClassification.Restricted, read: "pay:read", write: "pay:write"));

    private static readonly ResourcePolicy Policy = ResourcePolicy.Define(p => p
        .Allow("view", r => r.OwnedByCaller()));

    private sealed class Order : IHasOwner
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public string Reference { get; set; } = string.Empty;

        [Classified(FieldClassification.Restricted)]
        public decimal Cost { get; set; }
    }

    private sealed class PolicyLookup : IResourcePolicyRegistry
    {
        public ResourcePolicy? Find(Type resourceType) => resourceType == typeof(Order) ? Policy : null;
    }

    private sealed class ProfileLookup(FieldSecurityProfile profile) : IFieldSecurityRegistry
    {
        public FieldSecurityProfile? Find(Type resourceType) => profile;
    }

    private sealed class User(Guid userId, params string[] permissions) : ICurrentUser
    {
        public Guid? UserId => userId;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public IReadOnlyList<string> Permissions => permissions;
    }

    private sealed class Scope : ICurrentDataScope
    {
        public bool IsUnrestricted => true;
        public IReadOnlyCollection<Guid> OrgUnitIds { get; } = [];
    }

    private static Order NewOrder() => new() { Id = s_recordId, OwnerId = s_owner, Reference = "PO-1", Cost = 900m };

    [Fact]
    public async Task A_grantee_reads_the_shared_record_but_its_classified_field_stays_masked()
    {
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.TenantId.Returns(s_company);
        var shares = new RecordShares(new InMemoryRecordShareStore(), tenant, TimeProvider.System, Options.Create(new RecordShareOptions()));
        await shares.CreateAsync(nameof(Order), s_recordId.ToString(), s_grantee, s_owner, TimeSpan.FromHours(1));

        var grantee = new User(s_grantee);
        var order = NewOrder();

        (await new ResourceAuthorizer(grantee, new Scope(), new PolicyLookup(), tenant: tenant, shares: shares)
            .AuthorizeAsync(order, "view")).IsAllowed.Should().BeTrue("the share opens the record");

        var projection = new FieldAuthorizer(grantee, new ProfileLookup(Profile)).Redact(NewOrder());

        projection.Reference.Should().Be("PO-1", "unclassified fields show");
        projection.Cost.Should().Be(0m, "a share grants no field permission, so the classified field is masked");
    }

    [Fact]
    public async Task The_field_permission_still_unmasks_the_field_for_a_grantee_who_holds_it()
    {
        var payroll = new User(s_grantee, "pay:read");

        var projection = new FieldAuthorizer(payroll, new ProfileLookup(Profile)).Redact(NewOrder());

        projection.Cost.Should().Be(900m, "holding the field's own permission is what unmasks it");
    }
}
