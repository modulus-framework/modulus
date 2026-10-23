using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The company administration routes (premodule 2), served in-process. The policy is open here so the tests exercise the routes' own
/// checks and the rules of <see cref="TenantManager"/> (seat limit, status, duplicate slug), not the permission.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantAdminEndpointTests : IAsyncLifetime
{
    private const string Route = "/api/tenancy/tenants";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _storeKeepAlive = new("DataSource=:memory:");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _storeKeepAlive.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddMultiTenancy(t => t.UseHeaderResolver());
        builder.Services.AddEfCoreTenantStore(o => o.UseSqlite(_storeKeepAlive));
        builder.Services.AddAuthorization(o => o.AddPolicy(TenantAdminEndpoints.ManagePermission, p => p.RequireAssertion(_ => true)));

        _app = builder.Build();
        _app.MapModulusTenantAdmin<TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();

        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _storeKeepAlive.Dispose();
    }

    [Fact]
    public async Task A_company_is_created_read_listed_and_refused_when_its_slug_is_taken()
    {
        var created = await _client.PostAsJsonAsync(Route, new TenantCreateRequest("Acme", "Acme Ltd", null));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var view = await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        view.GetProperty("slug").GetString().Should().Be("acme", "the slug is stored lower-case");
        view.GetProperty("status").GetString().Should().Be("Active");
        view.GetProperty("activeMembers").GetInt32().Should().Be(0);
        var id = view.GetProperty("id").GetGuid();

        (await _client.GetAsync($"{Route}/{id:D}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync(Route, new TenantCreateRequest("acme", null, null))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.PostAsJsonAsync(Route, new TenantCreateRequest("Not A Slug", null, null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var listed = await (await _client.GetAsync($"{Route}?search=acm")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        listed.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_suspended_company_is_recorded_and_a_trial_needs_an_end_date()
    {
        var id = await CreateAsync("globex");

        var suspended = await _client.PutAsJsonAsync($"{Route}/{id:D}/status", new TenantStatusRequest("Suspended", null));
        suspended.StatusCode.Should().Be(HttpStatusCode.OK);
        (await suspended.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("status").GetString().Should().Be("Suspended");

        (await _client.PutAsJsonAsync($"{Route}/{id:D}/status", new TenantStatusRequest("Trial", null))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await _client.PutAsJsonAsync($"{Route}/{id:D}/status", new TenantStatusRequest("Frozen", null))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await _client.PutAsJsonAsync($"{Route}/{Guid.NewGuid():D}/status", new TenantStatusRequest("Active", null))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_seat_limit_refuses_a_new_member_with_409_and_a_removed_member_stays_on_the_list_as_inactive()
    {
        var id = await CreateAsync("initech");
        (await _client.PutAsJsonAsync($"{Route}/{id:D}/seats", new TenantSeatsRequest(1))).StatusCode.Should().Be(HttpStatusCode.OK);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        (await _client.PostAsJsonAsync($"{Route}/{id:D}/members", new TenantMemberRequest(first))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var full = await _client.PostAsJsonAsync($"{Route}/{id:D}/members", new TenantMemberRequest(second));
        full.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await full.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("error").GetString().Should().Be("SEAT_LIMIT");

        (await _client.DeleteAsync($"{Route}/{id:D}/members/{first:D}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var members = (await (await _client.GetAsync($"{Route}/{id:D}/members")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).EnumerateArray().ToList();
        members.Should().ContainSingle(m => m.GetProperty("userId").GetGuid() == first && !m.GetProperty("isActive").GetBoolean());

        (await _client.PostAsJsonAsync($"{Route}/{id:D}/members", new TenantMemberRequest(second))).StatusCode
            .Should().Be(HttpStatusCode.NoContent, "the revoked seat is free again");
    }

    [Fact]
    public async Task A_negative_seat_limit_is_refused_and_an_unknown_company_has_no_members_to_list()
    {
        var id = await CreateAsync("umbrella");

        (await _client.PutAsJsonAsync($"{Route}/{id:D}/seats", new TenantSeatsRequest(-1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.GetAsync($"{Route}/{Guid.NewGuid():D}/members")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Guid> CreateAsync(string slug)
    {
        var response = await _client.PostAsJsonAsync(Route, new TenantCreateRequest(slug, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
    }
}
