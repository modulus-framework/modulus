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
/// Company creation with the provisioning baseline (D-10): the baseline runs for the new company, the designated administrator becomes its
/// member after that, an invalid baseline is reported without hiding the company, and a duplicate slug provisions nothing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantCompanyProvisioningTests : IAsyncLifetime
{
    private const string Route = "/api/tenancy/tenants";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _storeKeepAlive = new("DataSource=:memory:");
    private readonly RecordingProvisioner _provisioner = new();
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

        var created = await _client.PostAsJsonAsync(Route, new TenantCreateRequest("acme", "Acme", null, administrator));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
        _provisioner.Provisioned.Should().ContainSingle(provisioned => provisioned == id);
        var members = (await (await _client.GetAsync($"{Route}/{id:D}/members")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).EnumerateArray().ToList();
        members.Should().ContainSingle(m => m.GetProperty("userId").GetGuid() == administrator && m.GetProperty("isActive").GetBoolean());
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

    private sealed class RecordingProvisioner : ICompanyProvisioner
    {
        public List<Guid> Provisioned { get; } = [];

        public Exception? Failure { get; set; }

        public Task ProvisionAsync(TenantInfo company, CancellationToken ct = default)
        {
            if (Failure is not null)
                throw Failure;
            Provisioned.Add(company.TenantId);
            return Task.CompletedTask;
        }
    }
}
