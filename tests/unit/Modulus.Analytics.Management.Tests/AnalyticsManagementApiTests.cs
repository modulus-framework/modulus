namespace Modulus.Analytics.Management.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Analytics.EntityFrameworkCore;
using Modulus.Analytics.Testing;
using Modulus.Authorization.Extensions;
using Xunit;

// Drives the API through a real TestServer host with an in-memory SQLite store. Authentication is header-driven:
// X-Test-Permissions grants the listed permissions.
[Trait("Category", "Unit")]
public sealed class AnalyticsManagementApiTests : IAsyncLifetime
{
    private const string Manage = "analytics:manage";
    private const string SecretValue = "123-45-6789-do-not-leak";

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = true);
        builder.Services.AddDbContext<TestDb>(o => o.UseSqlite(_connection));
        builder.Services.AddSingleton<IAnalyticsSink>(new InMemoryAnalyticsSink());
        builder.Services.AddModulusAnalytics(a => a
            .AddDataset(new AnalyticsDatasetDescriptor("orders", 1, "sales", [new("order_id", AnalyticsFieldType.Int64)], FreshnessSla: TimeSpan.FromMinutes(1)))
            .Services.AddScoped<IAnalyticsSource, StubSource>());
        builder.Services.AddModulusAnalyticsStore<TestDb>(new ConfigurationBuilder().Build(), o => o.EnableDelivery = false, o => o.EnableWorker = false);
        builder.Services.AddModulusAnalyticsManagement();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusAnalyticsManagement();
        await _app.StartAsync();

        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TestDb>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void Managing_analytics_delivery_is_a_critical_permission()
    {
        var registry = _app.Services.GetRequiredService<Modulus.Core.Abstractions.IPermissionRegistry>();

        registry.GetAll().Should().ContainSingle(d => d.Permission == Manage)
            .Which.Sensitivity.Should().Be(Modulus.Core.Abstractions.PermissionSensitivity.Critical);
    }

    [Fact]
    public async Task Anonymous_callers_are_refused_and_callers_without_the_permission_are_forbidden()
    {
        using var anonymous = _app.GetTestClient();
        using var other = AsUser("orders:read");

        (await anonymous.GetAsync("/analytics/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await other.GetAsync("/analytics/status")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PostAsJsonAsync("/analytics/dead-letters/replay", new AnalyticsReplayRequest([Guid.NewGuid()]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.DeleteAsync("/analytics/dead-letters/purge")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync("/analytics/backfills")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync("/analytics/freshness")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PostAsJsonAsync("/analytics/reconciliations", new AnalyticsReconcileRequest("orders", 1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonymous.PostAsJsonAsync("/analytics/reconciliations", new AnalyticsReconcileRequest("orders", 1))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/analytics/freshness")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await other.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/analytics/backfills")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reconciliation_reports_a_warehouse_that_is_short_and_a_gap_that_is_only_in_flight()
    {
        var short1 = Guid.NewGuid();
        var inFlight = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await SeedAsync(tenant: inFlight);
        }

        using var client = AsUser(Manage);

        var response = await client.PostAsJsonAsync("/analytics/reconciliations", new AnalyticsReconcileRequest("orders", 1, [short1, inFlight]));
        var body = await response.Content.ReadAsStringAsync();
        var report = await response.Content.ReadFromJsonAsync<AnalyticsReconciliationReport>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"status\":\"Missing\"").And.Contain("\"status\":\"InFlight\"");
        report!.Results.Single(r => r.TenantId == short1).Should().Match<AnalyticsReconciliationResult>(r => r.Status == AnalyticsReconciliationStatus.Missing && r.SourceCount == 3 && r.SinkCount == 0 && r.Sink == "memory");
        report.Results.Single(r => r.TenantId == inFlight).Should().Match<AnalyticsReconciliationResult>(r => r.Status == AnalyticsReconciliationStatus.InFlight && r.Pending == 3);
        report.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Bad_reconciliation_requests_are_refused_with_a_reason()
    {
        using var client = AsUser(Manage);

        var unknown = await client.PostAsJsonAsync("/analytics/reconciliations", new AnalyticsReconcileRequest("nope", 1, [Guid.NewGuid()]));
        var noVersion = await client.PostAsJsonAsync("/analytics/reconciliations", new AnalyticsReconcileRequest("orders", 0));
        var empty = await client.PostAsJsonAsync("/analytics/reconciliations", new AnalyticsReconcileRequest("orders", 1, [Guid.Empty]));
        var tooMany = await client.PostAsJsonAsync("/analytics/reconciliations", new AnalyticsReconcileRequest("orders", 1, [.. Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid())]));

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("not registered");
        noVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync()).Should().Contain("empty tenant");
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Freshness_shows_each_datasets_lag_against_its_sla_with_the_state_as_text()
    {
        await SeedAsync(createdMinutesAgo: 5);
        using var client = AsUser(Manage);

        var body = await client.GetStringAsync("/analytics/freshness");
        var list = await client.GetFromJsonAsync<List<AnalyticsDatasetFreshness>>("/analytics/freshness");

        body.Should().Contain("\"state\":\"Stale\"");
        var orders = list!.Single();
        orders.Dataset.Should().Be("orders");
        orders.State.Should().Be(AnalyticsFreshnessState.Stale);
        orders.Pending.Should().Be(1);
        orders.Lag.TotalSeconds.Should().BeInRange(295, 400);
        orders.FreshnessSla.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Starting_a_backfill_creates_a_run_per_tenant_and_lists_them_without_cursors()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        using var client = AsUser(Manage);

        var start = await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1, [a, b]));
        var started = await start.Content.ReadFromJsonAsync<AnalyticsBackfillStartResult>();
        var again = await (await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1, [a, b]))).Content.ReadFromJsonAsync<AnalyticsBackfillStartResult>();
        await SeedRunCursorAsync(a, "secret-cursor-position");
        var body = await client.GetStringAsync("/analytics/backfills");
        var runs = await client.GetFromJsonAsync<List<AnalyticsBackfillRunInfo>>("/analytics/backfills?dataset=orders");

        start.StatusCode.Should().Be(HttpStatusCode.OK);
        started.Should().Be(new AnalyticsBackfillStartResult(2, 0, 0));
        again.Should().Be(new AnalyticsBackfillStartResult(0, 0, 2));
        runs!.Select(r => r.TenantId).Should().BeEquivalentTo([a, b]);
        runs.Should().OnlyContain(r => r.Status == "Pending" && r.Dataset == "orders" && r.RequestedBy != null);
        body.Should().NotContain("secret-cursor-position").And.NotContainEquivalentOf("cursor");
    }

    [Fact]
    public async Task A_finished_backfill_is_restarted_only_when_asked()
    {
        var tenant = Guid.NewGuid();
        using var client = AsUser(Manage);
        await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1, [tenant]));
        await CompleteRunAsync(tenant);

        var skipped = await (await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1, [tenant]))).Content.ReadFromJsonAsync<AnalyticsBackfillStartResult>();
        var restarted = await (await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1, [tenant], Restart: true))).Content.ReadFromJsonAsync<AnalyticsBackfillStartResult>();
        var runs = await client.GetFromJsonAsync<List<AnalyticsBackfillRunInfo>>("/analytics/backfills");

        skipped.Should().Be(new AnalyticsBackfillStartResult(0, 0, 1));
        restarted.Should().Be(new AnalyticsBackfillStartResult(0, 1, 0));
        runs!.Single().Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Bad_backfill_requests_are_refused_with_a_reason()
    {
        using var client = AsUser(Manage);

        var unknown = await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("nope", 1, [Guid.NewGuid()]));
        var noVersion = await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 0));
        var empty = await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1, [Guid.Empty]));
        var tooMany = await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1, [.. Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid())]));

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("not registered");
        noVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync()).Should().Contain("empty tenant");
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_backfill_list_filters_by_status_and_refuses_an_unknown_status()
    {
        var done = Guid.NewGuid();
        using var client = AsUser(Manage);
        await client.PostAsJsonAsync("/analytics/backfills", new AnalyticsBackfillRequest("orders", 1, [done, Guid.NewGuid()]));
        await CompleteRunAsync(done);

        var completed = await client.GetFromJsonAsync<List<AnalyticsBackfillRunInfo>>("/analytics/backfills?status=completed");
        var pending = await client.GetFromJsonAsync<List<AnalyticsBackfillRunInfo>>("/analytics/backfills?status=Pending");

        completed!.Should().ContainSingle().Which.TenantId.Should().Be(done);
        pending!.Should().ContainSingle().Which.TenantId.Should().NotBe(done);
        (await client.GetAsync("/analytics/backfills?status=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Status_counts_each_state_and_ages_the_oldest_pending_record()
    {
        await SeedAsync(createdMinutesAgo: 10);
        await SeedAsync(createdMinutesAgo: 2);
        await SeedAsync(delivered: true);
        await SeedAsync(deadLettered: true);
        await SeedAsync(deadLettered: true);
        using var client = AsUser(Manage);

        var status = await client.GetFromJsonAsync<AnalyticsQueueStatus>("/analytics/status");

        status!.Pending.Should().Be(2);
        status.Delivered.Should().Be(1);
        status.DeadLettered.Should().Be(2);
        status.OldestPendingAgeSeconds.Should().BeInRange(590, 700);
    }

    [Fact]
    public async Task Status_of_an_empty_queue_has_no_oldest_record()
    {
        using var client = AsUser(Manage);

        var status = await client.GetFromJsonAsync<AnalyticsQueueStatus>("/analytics/status");

        status.Should().BeEquivalentTo(new AnalyticsQueueStatus(0, 0, 0, null, null));
    }

    [Fact]
    public async Task The_list_holds_only_dead_letters_and_filters_by_dataset_and_tenant()
    {
        var tenant = Guid.NewGuid();
        var orders = await SeedAsync(deadLettered: true, dataset: "orders", tenant: tenant);
        await SeedAsync(deadLettered: true, dataset: "invoices");
        await SeedAsync(dataset: "orders");
        using var client = AsUser(Manage);

        var all = await client.GetFromJsonAsync<AnalyticsPage<AnalyticsDeadLetter>>("/analytics/dead-letters");
        var byDataset = await client.GetFromJsonAsync<AnalyticsPage<AnalyticsDeadLetter>>("/analytics/dead-letters?dataset=orders");
        var byTenant = await client.GetFromJsonAsync<AnalyticsPage<AnalyticsDeadLetter>>($"/analytics/dead-letters?tenant={tenant}");

        all!.Total.Should().Be(2);
        byDataset!.Items.Should().ContainSingle().Which.Id.Should().Be(orders);
        byTenant!.Items.Should().ContainSingle().Which.TenantId.Should().Be(tenant);
    }

    [Fact]
    public async Task The_list_pages()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(deadLettered: true);
        }

        using var client = AsUser(Manage);
        var second = await client.GetFromJsonAsync<AnalyticsPage<AnalyticsDeadLetter>>("/analytics/dead-letters?page=2&pageSize=2");

        second!.Total.Should().Be(5);
        second.Items.Should().HaveCount(2);
        second.Page.Should().Be(2);
    }

    [Fact]
    public async Task Detail_shows_metadata_and_the_error_but_never_the_record_values()
    {
        var id = await SeedAsync(deadLettered: true, error: "sink said no");
        using var client = AsUser(Manage);

        var response = await client.GetAsync($"/analytics/dead-letters/{id}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("sink said no");
        body.Should().NotContain(SecretValue).And.NotContainEquivalentOf("payload");
        (await client.GetAsync($"/analytics/dead-letters/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_list_does_not_leak_values_either()
    {
        await SeedAsync(deadLettered: true);
        using var client = AsUser(Manage);

        var body = await client.GetStringAsync("/analytics/dead-letters");

        body.Should().NotContain(SecretValue).And.NotContainEquivalentOf("payload");
    }

    [Fact]
    public async Task Detail_of_a_record_that_is_not_dead_lettered_is_not_found()
    {
        var pending = await SeedAsync();
        using var client = AsUser(Manage);

        (await client.GetAsync($"/analytics/dead-letters/{pending}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Replay_requeues_dead_letters_and_counts_ids_that_are_not_dead_letters()
    {
        var dead = await SeedAsync(deadLettered: true, attempts: 7, error: "kept for history");
        var pending = await SeedAsync();
        using var client = AsUser(Manage);

        var response = await client.PostAsJsonAsync("/analytics/dead-letters/replay", new AnalyticsReplayRequest([dead, pending, Guid.NewGuid(), dead]));
        var result = await response.Content.ReadFromJsonAsync<AnalyticsReplayResponse>();

        result.Should().BeEquivalentTo(new AnalyticsReplayResponse(1, 2));
        var row = await RowAsync(dead);
        row.DeadLetteredAt.Should().BeNull();
        row.Status.Should().Be(AnalyticsDeliveryStatus.Pending);
        row.AttemptCount.Should().Be(0);
        row.NextAttemptAt.Should().NotBeNull();
        row.LastError.Should().Be("kept for history");
        (await RowAsync(pending)).AttemptCount.Should().Be(0, "a pending row is left alone");
    }

    [Fact]
    public async Task Replay_refuses_an_empty_or_oversized_request()
    {
        using var client = AsUser(Manage);

        (await client.PostAsJsonAsync("/analytics/dead-letters/replay", new AnalyticsReplayRequest([]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/analytics/dead-letters/replay", new AnalyticsReplayRequest([.. Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid())])))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Purge_deletes_only_dead_letters_older_than_the_cutoff()
    {
        var old = await SeedAsync(deadLettered: true, deadLetteredDaysAgo: 40);
        var recent = await SeedAsync(deadLettered: true, deadLetteredDaysAgo: 2);
        var pendingOld = await SeedAsync(createdMinutesAgo: 60 * 24 * 90);
        using var client = AsUser(Manage);

        var result = await (await client.DeleteAsync("/analytics/dead-letters/purge?beforeDays=30")).Content.ReadFromJsonAsync<AnalyticsPurgeResponse>();

        result!.Purged.Should().Be(1);
        (await ExistsAsync(old)).Should().BeFalse();
        (await ExistsAsync(recent)).Should().BeTrue();
        (await ExistsAsync(pendingOld)).Should().BeTrue("only dead letters are purged");
    }

    private HttpClient AsUser(string permissions)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task<Guid> SeedAsync(
        bool deadLettered = false, bool delivered = false, string dataset = "orders", Guid? tenant = null, string? error = null,
        int attempts = 0, int createdMinutesAgo = 5, int deadLetteredDaysAgo = 1)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDb>();
        var now = DateTime.UtcNow;
        var row = new AnalyticsDelivery
        {
            TenantId = tenant ?? Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Dataset = dataset,
            SchemaVersion = 1,
            OccurredAt = now.AddMinutes(-createdMinutesAgo),
            Payload = $"{{\"ssn\":\"{SecretValue}\"}}",
            CreatedAt = now.AddMinutes(-createdMinutesAgo),
            DeadLetteredAt = deadLettered ? now.AddDays(-deadLetteredDaysAgo) : null,
            DeliveredAt = delivered ? now : null,
            AttemptCount = attempts,
            LastError = error,
        };
        db.AnalyticsDeliveries.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<AnalyticsDelivery> RowAsync(Guid id)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestDb>().AnalyticsDeliveries.AsNoTracking().SingleAsync(d => d.Id == id);
    }

    private async Task<bool> ExistsAsync(Guid id)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestDb>().AnalyticsDeliveries.AnyAsync(d => d.Id == id);
    }

    private async Task SeedRunCursorAsync(Guid tenant, string cursor)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TestDb>().AnalyticsBackfillRuns
            .Where(r => r.TenantId == tenant).ExecuteUpdateAsync(s => s.SetProperty(r => r.Cursor, cursor));
    }

    private async Task CompleteRunAsync(Guid tenant)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TestDb>().AnalyticsBackfillRuns
            .Where(r => r.TenantId == tenant)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, AnalyticsBackfillStatus.Completed).SetProperty(r => r.CompletedAt, (DateTime?)DateTime.UtcNow));
    }

    private sealed class StubSource : IAnalyticsCountingSource
    {
        public Task<long> CountAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(3L);

        public string Dataset => "orders";

        public int Version => 1;

        public Task<AnalyticsExtractPage> ExtractAsync(AnalyticsExtractRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new AnalyticsExtractPage([], null));
    }

    private sealed class TestDb(DbContextOptions<TestDb> options) : ModulusAnalyticsDbContext(options);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            claims.AddRange(Request.Headers["X-Test-Permissions"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }
}
