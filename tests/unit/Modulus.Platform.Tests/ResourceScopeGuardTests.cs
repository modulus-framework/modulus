using FluentAssertions;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataQuality;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The resulting-scope guard checks the record a write will produce, not the endpoint's permission: the tenant, the org unit and the
/// permission's own scope. An unset org unit is a missing attribute. Refusals name the attribute or the rule, never the record's values.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ResourceScopeGuardTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Unit = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid ForeignUnit = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public async Task A_new_record_in_an_org_unit_in_scope_is_allowed()
    {
        var guard = Guard(company: Company, units: [Unit]);

        (await guard.CheckResultingScopeAsync(new Order { TenantId = Company, OrgUnitId = Unit }, "create")).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task A_new_record_in_a_unit_outside_the_callers_scope_is_refused_without_revealing_the_unit()
    {
        var guard = Guard(company: Company, units: [Unit]);

        var decision = await guard.CheckResultingScopeAsync(new Order { TenantId = Company, OrgUnitId = ForeignUnit }, "create");

        decision.IsAllowed.Should().BeFalse();
        decision.Code.Should().Be(AccessReasonCodes.OutOfScope);
        decision.Reason.Should().NotContain(ForeignUnit.ToString());
    }

    [Fact]
    public async Task An_unset_org_unit_is_a_missing_attribute_and_is_reported()
    {
        var sink = new RecordingSink();
        var guard = Guard(company: Company, units: [Unit], sink: sink);

        var decision = await guard.CheckResultingScopeAsync(new Order { TenantId = Company }, "create");

        decision.Code.Should().Be(AccessReasonCodes.MissingAttribute);
        sink.Findings.Single().MissingAttributes.Should().Equal("OrgUnitId");
    }

    [Fact]
    public async Task An_unrestricted_caller_may_create_a_record_with_no_unit()
    {
        var guard = Guard(company: Company, unrestricted: true);

        (await guard.CheckResultingScopeAsync(new Order { TenantId = Company }, "create")).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task A_record_owned_by_another_company_is_refused_even_within_the_callers_units()
    {
        var guard = Guard(company: Company, units: [Unit]);

        var decision = await guard.CheckResultingScopeAsync(new Order { TenantId = OtherCompany, OrgUnitId = Unit }, "create");

        decision.Code.Should().Be(AccessReasonCodes.OutOfScope);
    }

    [Fact]
    public async Task A_caller_with_no_company_may_not_write_a_company_owned_record()
    {
        var guard = Guard(company: null, unrestricted: true);

        (await guard.CheckResultingScopeAsync(new Order { TenantId = Company, OrgUnitId = Unit }, "create"))
            .Code.Should().Be(AccessReasonCodes.OutOfScope);
    }

    [Fact]
    public async Task The_host_context_sees_across_companies()
    {
        var guard = Guard(company: null, unrestricted: true, isHost: true);

        (await guard.CheckResultingScopeAsync(new Order { TenantId = OtherCompany, OrgUnitId = Unit }, "create")).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task A_permission_scope_narrower_than_the_company_refuses_a_record_it_does_not_cover()
    {
        var guard = Guard(company: Company, units: [Unit], scopes: new StubScopes(covers: false));

        var decision = await guard.CheckResultingScopeAsync(new Order { TenantId = Company, OrgUnitId = Unit }, "create", "sales:order:create");

        decision.Code.Should().Be(AccessReasonCodes.OutOfScope);
        decision.Reason.Should().Contain("sales:order:create");
    }

    [Fact]
    public async Task An_update_that_moves_a_record_out_of_scope_is_refused_on_the_resulting_record()
    {
        var guard = Guard(company: Company, units: [Unit]);
        var order = new Order { TenantId = Company, OrgUnitId = Unit };
        (await guard.CheckResultingScopeAsync(order, "edit")).IsAllowed.Should().BeTrue("the record starts in scope");

        order.OrgUnitId = ForeignUnit;

        (await guard.CheckResultingScopeAsync(order, "edit")).Code.Should().Be(AccessReasonCodes.OutOfScope);
    }

    [Fact]
    public async Task The_throwing_form_raises_forbidden_with_the_reason_code_and_no_record_values()
    {
        var guard = Guard(company: Company, units: [Unit]);

        var act = () => guard.EnsureResultingScopeAsync(new Order { TenantId = Company, OrgUnitId = ForeignUnit }, "create");

        var error = (await act.Should().ThrowAsync<ForbiddenException>()).Which;
        error.Message.Should().Contain(AccessReasonCodes.OutOfScope);
        error.Message.Should().NotContain(ForeignUnit.ToString());
    }

    [Fact]
    public async Task A_type_with_no_org_unit_is_not_subject_to_the_org_rule()
    {
        var guard = Guard(company: Company, units: []);

        (await guard.CheckResultingScopeAsync(new Plain(), "create")).IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task A_dimension_value_the_permission_needs_but_the_record_lacks_is_a_missing_attribute_and_is_reported()
    {
        var sink = new RecordingSink();
        var guard = Guard(company: Company, units: [Unit], scopes: new AssignedScopes("warehouse"), sink: sink, maps: WarehouseMaps());

        var decision = await guard.CheckResultingScopeAsync(new Shipment { TenantId = Company }, "create", "shipments:create");

        decision.Code.Should().Be(AccessReasonCodes.MissingAttribute);
        sink.Findings.Single().MissingAttributes.Should().Equal("warehouse");
        decision.Reason.Should().NotContain(Company.ToString());
    }

    [Fact]
    public async Task A_dimension_value_set_on_the_record_but_outside_the_scope_is_out_of_scope_not_missing()
    {
        var sink = new RecordingSink();
        var guard = Guard(company: Company, units: [Unit], scopes: new AssignedScopes("warehouse"), sink: sink, maps: WarehouseMaps());

        var decision = await guard.CheckResultingScopeAsync(
            new Shipment { TenantId = Company, WarehouseId = ForeignUnit }, "create", "shipments:create");

        decision.Code.Should().Be(AccessReasonCodes.OutOfScope);
        sink.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task A_key_the_map_does_not_declare_is_not_reported_as_missing()
    {
        var sink = new RecordingSink();
        var guard = Guard(company: Company, units: [Unit], scopes: new AssignedScopes("customer"), sink: sink, maps: WarehouseMaps());

        var decision = await guard.CheckResultingScopeAsync(new Shipment { TenantId = Company }, "create", "shipments:create");

        decision.Code.Should().Be(AccessReasonCodes.OutOfScope);
        sink.Findings.Should().BeEmpty();
    }

    private static ScopeMapRegistry WarehouseMaps()
    {
        var builder = new ScopeMapBuilder<Shipment>();
        builder.AssignedKey("warehouse", s => s.WarehouseId);
        return new ScopeMapRegistry([builder.Build()]);
    }

    private static ResourceScopeGuard Guard(
        Guid? company,
        Guid[]? units = null,
        bool unrestricted = false,
        bool isHost = false,
        IScopeEnforcer? scopes = null,
        RecordingSink? sink = null,
        IScopeMapRegistry? maps = null)
        => new(new StubTenant(company, isHost), new StubScope(unrestricted, units ?? []), scopes, sink, maps);

    private sealed class Shipment : IHasTenantId
    {
        public Guid TenantId { get; set; }
        public Guid? WarehouseId { get; set; }
    }

    private sealed class Order : IHasTenantId, IHasOrgUnit
    {
        public Guid TenantId { get; set; }
        public Guid OrgUnitId { get; set; }
    }

    private sealed class Plain
    {
    }

    private sealed class RecordingSink : IDataQualityFindingSink
    {
        public List<DataQualityFinding> Findings { get; } = [];

        public void Record(DataQualityFinding finding) => Findings.Add(finding);
    }

    private sealed class StubTenant(Guid? company, bool isHost) : ICurrentTenant
    {
        public Guid? TenantId => company;

        public string? TenantSlug => null;

        public bool IsAvailable => company is not null;

        public bool IsHost => isHost;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }

    private sealed class StubScope(bool unrestricted, Guid[] units) : ICurrentDataScope
    {
        public bool IsUnrestricted => unrestricted;

        public IReadOnlyCollection<Guid> OrgUnitIds => units;
    }

    /// <summary>A caller whose permission is assigned-scoped to one assignment type, and who is in scope for no record.</summary>
    private sealed class AssignedScopes(string assignmentType) : IScopeEnforcer
    {
        public ResolvedScope Describe(string permission)
            => ResolvedScope.Of([new ScopeClause([new PermissionScope(ScopeKind.Assigned, assignmentType)])]);

        public IQueryable<T> Apply<T>(IQueryable<T> source, string permission) where T : class => throw new NotSupportedException();

        public bool IsInScope(object record, string permission) => false;
    }

    private sealed class StubScopes(bool covers) : IScopeEnforcer
    {
        public ResolvedScope Describe(string permission) => ResolvedScope.None;

        public IQueryable<T> Apply<T>(IQueryable<T> source, string permission) where T : class => throw new NotSupportedException();

        public bool IsInScope(object record, string permission) => covers;
    }
}
