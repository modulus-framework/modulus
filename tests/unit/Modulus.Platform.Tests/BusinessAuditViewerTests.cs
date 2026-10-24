using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.Core.Null;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The business audit viewer (premodule 4): search by entity, actor and time, and read one entry, scoped to the caller's company. The
/// policy is open here so the tests exercise the company scope, not the permission.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BusinessAuditViewerTests : IAsyncDisposable
{
    private static readonly Guid CompanyA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CompanyB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Alice = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid Bob = Guid.Parse("00000000-0000-0000-0000-00000000b002");

    private readonly InMemoryAuditLogStore _store = new();
    private WebApplication? _app;
    private HttpClient? _client;
    private Guid _aOrderEntry;
    private Guid _bOrderEntry;

    private async Task<HttpClient> StartAsync(ICurrentTenant tenant)
    {
        await _store.AppendAsync(Entry(CompanyA, Alice, "Order", "order.created"));
        await _store.AppendAsync(Entry(CompanyA, Bob, "Invoice", "invoice.posted"));
        await _store.AppendAsync(Entry(CompanyB, Alice, "Order", "order.created"));
        _aOrderEntry = (await _store.QueryAsync(new AuditLogQuery { TenantId = CompanyA, Resource = "Order" })).Items.Single().Id;
        _bOrderEntry = (await _store.QueryAsync(new AuditLogQuery { TenantId = CompanyB, Resource = "Order" })).Items.Single().Id;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IAuditLogStore>(_store);
        builder.Services.AddSingleton(tenant);
        builder.Services.AddSingleton<ICurrentUser, NullCurrentUser>();
        builder.Services.AddAuthorization(o => o.AddPolicy(BusinessAuditViewerEndpoints.ViewPermission, p => p.RequireAssertion(_ => true)));

        _app = builder.Build();
        _app.MapModulusBusinessAuditViewer();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
        return _client;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static AuditLogEntry Entry(Guid company, Guid user, string resource, string action) => new()
    {
        OccurredAt = DateTimeOffset.UtcNow,
        TenantId = company,
        UserId = user,
        Action = action,
        Resource = resource,
    };

    [Fact]
    public async Task A_company_sees_only_its_own_entries()
    {
        var client = await StartAsync(new FixedTenant(CompanyA));

        var page = await client.GetFromJsonAsync<PagedList<BusinessAuditView>>("/api/audit/business");

        page!.TotalCount.Should().Be(2);
        page.Items.Should().OnlyContain(e => e.TenantId == CompanyA);
    }

    [Fact]
    public async Task Entity_actor_and_action_filter_within_the_company()
    {
        var client = await StartAsync(new FixedTenant(CompanyA));

        var byEntity = await client.GetFromJsonAsync<PagedList<BusinessAuditView>>("/api/audit/business?entity=Order");
        byEntity!.Items.Select(e => e.Action).Should().Equal("order.created");

        var byActor = await client.GetFromJsonAsync<PagedList<BusinessAuditView>>($"/api/audit/business?actor={Bob:D}");
        byActor!.Items.Select(e => e.Action).Should().Equal("invoice.posted");

        var byAction = await client.GetFromJsonAsync<PagedList<BusinessAuditView>>("/api/audit/business?action=posted");
        byAction!.Items.Should().ContainSingle().Which.Resource.Should().Be("Invoice");
    }

    [Fact]
    public async Task Another_companys_entry_answers_not_found_as_a_missing_one_does()
    {
        var client = await StartAsync(new FixedTenant(CompanyA));

        (await client.GetAsync($"/api/audit/business/{_aOrderEntry:D}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/audit/business/{_bOrderEntry:D}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/audit/business/{Guid.NewGuid():D}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_host_context_sees_every_company()
    {
        var client = await StartAsync(new FixedTenant(null, isHost: true));

        var page = await client.GetFromJsonAsync<PagedList<BusinessAuditView>>("/api/audit/business");

        page!.TotalCount.Should().Be(3);
        (await client.GetAsync($"/api/audit/business/{_bOrderEntry:D}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_request_with_no_company_resolved_sees_nothing()
    {
        var client = await StartAsync(new FixedTenant(null));

        var page = await client.GetFromJsonAsync<PagedList<BusinessAuditView>>("/api/audit/business");

        page!.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
        (await client.GetAsync($"/api/audit/business/{_aOrderEntry:D}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class FixedTenant : ICurrentTenant
    {
        public FixedTenant(Guid? tenantId, bool isHost = false)
        {
            TenantId = tenantId;
            IsHost = isHost;
        }

        public Guid? TenantId { get; }
        public string? TenantSlug => null;
        public bool IsAvailable => TenantId is not null || IsHost;
        public bool IsHost { get; }

        public IDisposable Change(TenantInfo? tenant) => new Restore();

        private sealed class Restore : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
