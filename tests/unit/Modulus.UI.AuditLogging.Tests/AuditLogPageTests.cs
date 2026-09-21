using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Modulus.AuditLogging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.Core.Null;
using Modulus.UI.AuditLogging.Pages.AuditLogs;
using NSubstitute;
using Xunit;

namespace Modulus.UI.AuditLogging.Tests;

/// <summary>
/// Spec for the audit-log browser (filter pass-through into the store query,
/// invalid user id rejected, tenant scoping) and details (entry dump, 404 for
/// unknown id, 404 for an id outside the ambient tenant).
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuditLogPageTests
{
    // NullCurrentTenant.IsHost is true, so these existing (non-tenant-focused)
    // tests keep their original "sees everything" behavior; tenant scoping is
    // exercised separately below with an explicit resolved/unresolved tenant.
    private static readonly ICurrentTenant Host = new NullCurrentTenant();

    private static IndexModel BuildIndex(IAuditLogStore store, ICurrentTenant? tenant = null)
        => new(store, tenant ?? Host, Options.Create(new AuditLoggingUiOptions()), new TestLocalizer());

    [Fact]
    public async Task Index_ForwardsFilters_ToStoreQuery()
    {
        var store = Substitute.For<IAuditLogStore>();
        store.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<AuditLogEntry>());
        var userId = Guid.NewGuid();
        var model = BuildIndex(store);
        model.Action = "OrderPlaced";
        model.UserId = userId.ToString();

        await model.OnGetAsync(default);

        await store.Received(1).QueryAsync(
            Arg.Is<AuditLogQuery>(q => q.Action == "OrderPlaced" && q.UserId == userId),
            Arg.Any<CancellationToken>());
        model.ModelState.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Index_InvalidUserId_RendersError_WithoutQuerying()
    {
        var store = Substitute.For<IAuditLogStore>();
        var model = BuildIndex(store);
        model.UserId = "not-a-guid";

        await model.OnGetAsync(default);

        model.ModelState.IsValid.Should().BeFalse();
        await store.DidNotReceiveWithAnyArgs().QueryAsync(
            default!, default);
    }

    [Fact]
    public async Task Details_KnownId_ReturnsPageWithEntry()
    {
        var entry = new AuditLogEntry
        {
            Action = "OrderPlaced",
            OccurredAt = DateTimeOffset.UtcNow,
        };
        var store = Substitute.For<IAuditLogStore>();
        store.GetOrNullAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var model = new DetailsModel(store, Host, new TestLocalizer());

        var result = await model.OnGetAsync(entry.Id, default);

        result.Should().BeOfType<PageResult>();
        model.Entry.Should().Be(entry);
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        var store = Substitute.For<IAuditLogStore>();
        store.GetOrNullAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((AuditLogEntry?)null);
        var model = new DetailsModel(store, Host, new TestLocalizer());

        (await model.OnGetAsync(Guid.NewGuid(), default)).Should().BeOfType<NotFoundResult>();
    }

    // ── Tenant isolation ─────────────────────────────────────────

    private sealed class FakeCurrentTenant(Guid? tenantId, bool isHost = false) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;
        public string? TenantSlug => null;
        public bool IsAvailable => tenantId is not null;
        public bool IsHost => isHost;
        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Index_ScopesTheQuery_ToTheAmbientTenant()
    {
        var store = Substitute.For<IAuditLogStore>();
        store.QueryAsync(Arg.Any<AuditLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedList<AuditLogEntry>());
        var tenantId = Guid.NewGuid();
        var model = BuildIndex(store, new FakeCurrentTenant(tenantId));

        await model.OnGetAsync(default);

        await store.Received(1).QueryAsync(
            Arg.Is<AuditLogQuery>(q => q.TenantId == tenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Index_WithNoTenantResolved_NeverQueries_SoNothingLeaksAcrossTenants()
    {
        var store = Substitute.For<IAuditLogStore>();
        var model = BuildIndex(store, new FakeCurrentTenant(tenantId: null, isHost: false));

        await model.OnGetAsync(default);

        model.Result.Items.Should().BeEmpty();
        await store.DidNotReceiveWithAnyArgs().QueryAsync(default!, default);
    }

    [Fact]
    public async Task Details_EntryFromAnotherTenant_IsNotFound()
    {
        var entry = new AuditLogEntry
        {
            Action = "Grant",
            OccurredAt = DateTimeOffset.UtcNow,
            TenantId = Guid.NewGuid(),
        };
        var store = Substitute.For<IAuditLogStore>();
        store.GetOrNullAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var model = new DetailsModel(store, new FakeCurrentTenant(Guid.NewGuid()), new TestLocalizer());

        var result = await model.OnGetAsync(entry.Id, default);

        result.Should().BeOfType<NotFoundResult>();
        model.Entry.Should().BeNull();
    }

    [Fact]
    public async Task Details_EntryFromTheSameTenant_IsVisible()
    {
        var tenantId = Guid.NewGuid();
        var entry = new AuditLogEntry
        {
            Action = "Grant",
            OccurredAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
        };
        var store = Substitute.For<IAuditLogStore>();
        store.GetOrNullAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var model = new DetailsModel(store, new FakeCurrentTenant(tenantId), new TestLocalizer());

        var result = await model.OnGetAsync(entry.Id, default);

        result.Should().BeOfType<PageResult>();
        model.Entry.Should().Be(entry);
    }
}
