using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// Company creation and the baseline (D-10, decision 30): the baseline runs for the new company, the designated administrator must be an
/// account in the identity store and becomes a member after the baseline, an invalid baseline is reported without hiding the company, a duplicate
/// slug provisions nothing, and the baseline can be re-applied to an existing company (never to a closed one).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantCompanyProvisioningTests : IAsyncLifetime
{
    private const string Route = "/api/tenancy/tenants";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _storeKeepAlive = new("DataSource=:memory:");
    private readonly RecordingProvisioner _provisioner = new();
    private readonly KnownAccounts _accounts = new();
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
        builder.Services.AddSingleton<ICompanyProvisioner>(_provisioner);
        builder.Services.AddSingleton<IUserRoleDirectory>(_accounts);
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
    public async Task The_baseline_runs_for_the_new_company_and_then_the_administrator_becomes_a_member()
    {
        var administrator = Guid.NewGuid();
        _accounts.Known.Add(administrator);

        var created = await _client.PostAsJsonAsync(Route, new TenantCreateRequest("acme", "Acme", null, administrator));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
        _provisioner.Provisioned.Should().ContainSingle(provisioned => provisioned == id);
        var members = (await (await _client.GetAsync($"{Route}/{id:D}/members")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).EnumerateArray().ToList();
        members.Should().ContainSingle(m => m.GetProperty("userId").GetGuid() == administrator && m.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task A_designated_administrator_must_be_an_account_before_the_company_is_created()
    {
        var response = await _client.PostAsJsonAsync(Route, new TenantCreateRequest("umbrella", null, null, Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("errors");
        errors.TryGetProperty("administratorUserId", out _).Should().BeTrue();
        _provisioner.Provisioned.Should().BeEmpty("nothing is provisioned for a company that was never created");
    }

    [Fact]
    public async Task An_invalid_baseline_answers_500_and_the_company_exists_with_no_grants()
    {
        _provisioner.Failure = new InvalidOperationException("The company baseline names 'orders:reed', which is not a registered permission.");

        var response = await _client.PostAsJsonAsync(Route, new TenantCreateRequest("globex", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("error").GetString().Should().Be("BASELINE_INVALID");
        var id = body.GetProperty("companyId").GetGuid();
        (await _client.GetAsync($"{Route}/{id:D}")).StatusCode.Should().Be(HttpStatusCode.OK, "the company is kept, so the fault can be fixed and its grants applied");
    }

    [Fact]
    public async Task A_duplicate_slug_is_refused_before_anything_is_provisioned()
    {
        await _client.PostAsJsonAsync(Route, new TenantCreateRequest("initech", null, null));
        var before = _provisioner.Provisioned.Count;

        var duplicate = await _client.PostAsJsonAsync(Route, new TenantCreateRequest("initech", null, null));

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _provisioner.Provisioned.Should().HaveCount(before);
    }

    [Fact]
    public async Task Re_applying_the_baseline_reports_the_roles_it_granted()
    {
        var id = await CreateCompanyAsync("hooli");
        _provisioner.Granted = ["Admin"];

        var response = await _client.PostAsync($"{Route}/{id:D}/baseline", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("companyId").GetGuid().Should().Be(id);
        body.GetProperty("grantedRoles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Admin");
        _provisioner.Provisioned.Should().HaveCount(2, "the company was provisioned at creation and again by the re-application");
    }

    [Fact]
    public async Task Re_applying_the_baseline_to_an_unknown_company_is_not_found()
    {
        var response = await _client.PostAsync($"{Route}/{Guid.NewGuid():D}/baseline", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_closed_company_does_not_resolve_so_it_gets_no_baseline()
    {
        var id = await CreateCompanyAsync("pied-piper");
        using (var scope = _app.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<TenantManager<TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>>();
            (await manager.SetStatusAsync(id, TenantStatus.Closed)).Should().BeTrue();
        }
        var provisionedBefore = _provisioner.Provisioned.Count;

        var response = await _client.PostAsync($"{Route}/{id:D}/baseline", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a closed company is inactive, so the store does not resolve it");
        _provisioner.Provisioned.Should().HaveCount(provisionedBefore);
    }

    private async Task<Guid> CreateCompanyAsync(string slug)
    {
        var created = await _client.PostAsJsonAsync(Route, new TenantCreateRequest(slug, null, null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
    }

    /// <summary>The identity store as the company endpoints see it: an account is known by its id, and has no roles in these tests.</summary>
    private sealed class KnownAccounts : IUserRoleDirectory
    {
        public HashSet<Guid> Known { get; } = [];

        public ValueTask<IReadOnlyCollection<string>?> GetRolesAsync(Guid userId, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyCollection<string>?>(Known.Contains(userId) ? new List<string>() : null);
    }

    private sealed class RecordingProvisioner : ICompanyProvisioner
    {
        public List<Guid> Provisioned { get; } = [];

        public Exception? Failure { get; set; }

        /// <summary>The roles the next provisioning reports as granted.</summary>
        public IReadOnlyList<string> Granted { get; set; } = [];

        public Task<IReadOnlyList<string>> ProvisionAsync(TenantInfo company, CancellationToken ct = default)
        {
            if (Failure is not null)
                throw Failure;
            Provisioned.Add(company.TenantId);
            return Task.FromResult(Granted);
        }
    }
}
