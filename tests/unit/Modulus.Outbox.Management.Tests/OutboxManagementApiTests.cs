using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Modulus.Authorization.Extensions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Outbox.Abstractions;
using Xunit;

namespace Modulus.Outbox.Management.Tests;

// Drives the dead-letter API through a real TestServer host with an in-memory SQLite outbox.
// Authentication is header-driven: X-Test-Permissions grants the listed permissions.
[Trait("Category", "Unit")]
public sealed class OutboxManagementApiTests : IAsyncLifetime
{
    private const int MaxRetries = 3;
    private const string Module = "Orders";
    private const string ManagePermission = "messaging:manage";

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Services
            .AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddModulusAuthorization();
        builder.Services.AddDbContext<OutboxTestDb>(o => o.UseSqlite(_connection));
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<OutboxTestDb>());
        builder.Services.Configure<OutboxOptions>(o => o.MaxRetries = MaxRetries);
        builder.Services.AddModulusOutboxManagement();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusOutboxManagement();
        await _app.StartAsync();

        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxTestDb>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        using var client = _app.GetTestClient();

        var response = await client.GetAsync("/outbox/dead-letters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_in_callers_without_the_manage_permission_are_forbidden()
    {
        using var client = AsUser("orders:read");

        var response = await client.GetAsync("/outbox/dead-letters");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_returns_only_dead_lettered_rows()
    {
        var dead = await SeedAsync(retryCount: MaxRetries);
        await SeedAsync(retryCount: MaxRetries - 1);                 // still retrying
        await SeedAsync(retryCount: MaxRetries, processed: true);    // already delivered

        using var client = AsUser(ManagePermission);
        var page = await client.GetFromJsonAsync<PaginatedResponse<OutboxDeadLetterListItem>>("/outbox/dead-letters");

        page!.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Id.Should().Be(dead);
    }

    [Fact]
    public async Task List_filters_by_module()
    {
        await SeedAsync(retryCount: MaxRetries, module: "Orders");
        await SeedAsync(retryCount: MaxRetries, module: "Billing");

        using var client = AsUser(ManagePermission);
        var page = await client.GetFromJsonAsync<PaginatedResponse<OutboxDeadLetterListItem>>(
            "/outbox/dead-letters?moduleFilter=Billing");

        page!.Total.Should().Be(1);
        page.Items.Single().ModuleName.Should().Be("Billing");
    }

    [Fact]
    public async Task Detail_returns_a_dead_letter_and_hides_healthy_rows()
    {
        var dead = await SeedAsync(retryCount: MaxRetries, error: "smtp down");
        var healthy = await SeedAsync(retryCount: 0);

        using var client = AsUser(ManagePermission);

        var detail = await client.GetFromJsonAsync<OutboxDeadLetterDetail>($"/outbox/dead-letters/{dead}");
        detail!.Error.Should().Be("smtp down");
        (await client.GetAsync($"/outbox/dead-letters/{healthy}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Replay_resets_a_dead_letter_so_it_is_retried()
    {
        var id = await SeedAsync(retryCount: MaxRetries, error: "boom");

        using var client = AsUser(ManagePermission);
        var response = await client.PostAsJsonAsync("/outbox/replay", new OutboxReplayRequest([id]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OutboxReplayResponse>();
        result!.ReplayedCount.Should().Be(1);
        result.NotFoundCount.Should().Be(0);

        using var scope = _app.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<OutboxTestDb>().Messages.AsNoTracking()
            .SingleAsync(m => m.Id == id);
        row.RetryCount.Should().Be(0);
        row.Error.Should().BeNull();
        row.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task Replay_reports_ids_that_are_not_dead_lettered_as_not_found()
    {
        var retrying = await SeedAsync(retryCount: 1);
        var unknown = Guid.NewGuid();

        using var client = AsUser(ManagePermission);
        var response = await client.PostAsJsonAsync("/outbox/replay", new OutboxReplayRequest([retrying, unknown]));
        var result = await response.Content.ReadFromJsonAsync<OutboxReplayResponse>();

        result!.ReplayedCount.Should().Be(0);
        result.NotFoundCount.Should().Be(2);
    }

    [Fact]
    public async Task Replay_rejects_an_empty_request()
    {
        using var client = AsUser(ManagePermission);

        var response = await client.PostAsJsonAsync("/outbox/replay", new OutboxReplayRequest([]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Purge_deletes_only_old_dead_letters()
    {
        var old = await SeedAsync(retryCount: MaxRetries, createdDaysAgo: 60);
        var recent = await SeedAsync(retryCount: MaxRetries, createdDaysAgo: 1);

        using var client = AsUser(ManagePermission);
        var response = await client.DeleteAsync("/outbox/dead-letters/purge?beforeDays=30");
        var result = await response.Content.ReadFromJsonAsync<OutboxPurgeResponse>();

        result!.PurgedCount.Should().Be(1);
        using var scope = _app.Services.CreateScope();
        var remaining = await scope.ServiceProvider.GetRequiredService<OutboxTestDb>().Messages
            .Select(m => m.Id).ToListAsync();
        remaining.Should().Contain(recent).And.NotContain(old);
    }

    private HttpClient AsUser(string permissions)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task<Guid> SeedAsync(
        int retryCount,
        bool processed = false,
        string module = Module,
        string? error = null,
        int createdDaysAgo = 0)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
        var message = new OutboxMessage
        {
            MessageType = "OrderPlaced",
            Payload = "{}",
            ModuleName = module,
            CreatedAt = DateTime.UtcNow.AddDays(-createdDaysAgo),
            RetryCount = retryCount,
            ProcessedAt = processed ? DateTime.UtcNow : null,
            Error = error,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private sealed class OutboxTestDb(DbContextOptions<OutboxTestDb> options) : DbContext(options)
    {
        public DbSet<OutboxMessage> Messages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<OutboxMessage>(b => b.HasKey(m => m.Id));
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            claims.AddRange(Request.Headers["X-Test-Permissions"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => new Claim("permission", p)));

            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
