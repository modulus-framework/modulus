using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Approval;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Management;
using Modulus.Authorization.Workflow;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>Multi-step approval: chain choice by amount, per-step permission, approval limits, separation of duties, races, escalation.</summary>
[Trait("Category", "Unit")]
public sealed class ApprovalWorkflowTests : IAsyncLifetime
{
    // A file, not ":memory:": each request opens its own connection, and an in-memory database is private to one connection.
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"modulus-approvals-{Guid.NewGuid():N}.db");
    private WebApplication _app = null!;
    private readonly ManualClock _clock = new();
    private readonly RecordingObserver _observer = new();

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class RecordingObserver : IApprovalWorkflowObserver
    {
        public List<string> Changes { get; } = [];

        public ValueTask OnChangedAsync(ApprovalRequest request, string change, CancellationToken ct = default)
        {
            lock (Changes)
                Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HttpUser(IHttpContextAccessor accessor) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

        public Guid? UserId => Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

        public string? UserName => null;

        public string? Email => null;

        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

        public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

        public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Permissions => [.. Principal?.FindAll("permission").Select(c => c.Value) ?? []];
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-UserId", out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
            claims.AddRange(Request.Headers["X-Test-Permissions"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddScoped<ICurrentUser, HttpUser>();
        // These hosts carry the caller's permissions as claims; the grant store is the source by default now.
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = true);
        builder.Services.AddEfCoreAuthorizationStores(o => o.UseSqlite($"Data Source={_databasePath}").AddInterceptors(new BusyWaitInterceptor()));
        builder.Services.AddModulusAuthorizationManagement();
        builder.Services.AddApprovalWorkflow(o => o.EnableEscalation = false);
        builder.Services.AddSingleton<IApprovalWorkflowObserver>(_observer);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusApprovalWorkflow();
        // What an app's own handler does: read the amount from the stored document, then submit.
        _app.MapPost("/test/submit", async (SubmitBody body, IApprovalWorkflow workflow) =>
        {
            try
            {
                var r = await workflow.SubmitAsync(new ApprovalSubmission(body.DocumentType, body.DocumentId, body.Amount, body.Currency));
                return Results.Ok(new { r.Id, Status = r.Status.ToString(), r.CurrentStep, CurrentStepName = r.Step?.Name, r.Escalated, Decisions = Array.Empty<object>() });
            }
            catch (ConflictException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 409);
            }
            catch (ValidationException ex)
            {
                return Results.Problem(detail: string.Join(" ", ex.Errors), statusCode: 400);
            }
        }).RequireAuthorization();

        using (var db = _app.Services.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext())
            db.Database.EnsureCreated();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    /// <summary>
    /// Two approvers can decide at the same moment. SQLite serialises their writes with a lock, and a writer that finds the
    /// database locked waits for it instead of failing at once; the workflow's conditional update then lets only one win.
    /// </summary>
    private sealed class BusyWaitInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => SetBusyTimeout(connection);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            SetBusyTimeout(connection);
            return Task.CompletedTask;
        }

        private static void SetBusyTimeout(DbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 10000;";
            command.ExecuteNonQuery();
        }
    }

    private sealed record SubmitBody(string DocumentType, string DocumentId, decimal Amount, string? Currency = null);

    private sealed record StepView(string Name, string Permission, int RequiredApprovals);

    private sealed record RequestView(Guid Id, string Status, int CurrentStep, string? CurrentStepName, bool Escalated, JsonElement[] Decisions);

    private HttpClient As(Guid user, params string[] permissions)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", user.ToString());
        if (permissions.Length > 0)
            client.DefaultRequestHeaders.Add("X-Test-Permissions", string.Join(',', permissions));
        return client;
    }

    private static readonly Guid s_admin = Guid.NewGuid();

    private HttpClient Admin() => As(s_admin, AuthorizationManagementExtensions.ManagePermission);

    private async Task<Guid> ChainAsync(string name, decimal? min, decimal? max, params object[] steps)
    {
        using var admin = Admin();
        var response = await admin.PutAsJsonAsync("/authorization/approvals/chains",
            new { name, documentType = "PurchaseOrder", minAmount = min, maxAmount = max, steps });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object Step(string name, string permission, int approvals = 1, double? escalateMinutes = null, string? escalation = null, bool checkAuthority = true)
        => new { name, permission, requiredApprovals = approvals, escalateAfterMinutes = escalateMinutes, escalationPermission = escalation, checkAuthority };

    private async Task LimitAsync(Guid user, string permission, decimal max)
        => await _app.Services.GetRequiredService<EfApprovalAuthorityStore>()
            .AddAsync(new ApprovalAuthority(GrantHolderType.User, user.ToString(), permission, max), null, DateTimeOffset.UtcNow);

    private async Task<RequestView> SubmitAsync(Guid requester, decimal amount, string documentId = "PO-1")
    {
        using var client = As(requester);
        var response = await client.PostAsJsonAsync("/test/submit", new SubmitBody("PurchaseOrder", documentId, amount));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RequestView>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private static Task<HttpResponseMessage> Act(HttpClient client, Guid id, string action, string? comment = null)
        => client.PostAsJsonAsync($"/authorization/approvals/requests/{id}/{action}", new { comment });

    private static async Task<RequestView> ViewAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RequestView>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()!;

    [Fact]
    public async Task A_large_order_goes_through_each_step_and_keeps_the_steps_it_started_with()
    {
        await ChainAsync("small", null, 999.99m, Step("Manager", "po:approve"));
        var large = await ChainAsync("large", 1000m, null, Step("Manager", "po:approve"), Step("Finance", "po:finance"));
        var requester = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var finance = Guid.NewGuid();
        await LimitAsync(manager, "po:approve", 10_000m);
        await LimitAsync(finance, "po:finance", 100_000m);

        var request = await SubmitAsync(requester, 5000m);
        request.CurrentStepName.Should().Be("Manager");

        // Editing the chain later does not change a request already under way.
        using var admin = Admin();
        (await admin.DeleteAsync($"/authorization/approvals/chains/{large}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var requesterClient = As(requester, "po:approve");
        var self = await Act(requesterClient, request.Id, "approve");
        self.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(self)).Should().StartWith("REQUESTER_CANNOT_APPROVE");

        using var managerClient = As(manager, "po:approve", "po:finance");
        (await ViewAsync(await Act(managerClient, request.Id, "approve", "ok"))).CurrentStepName.Should().Be("Finance");

        // One person, one step: the manager cannot also take the finance step.
        var twice = await Act(managerClient, request.Id, "approve");
        twice.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(twice)).Should().StartWith("ALREADY_ACTED");

        using var financeClient = As(finance, "po:finance");
        var done = await ViewAsync(await Act(financeClient, request.Id, "approve"));
        done.Status.Should().Be("Approved");
        done.Decisions.Should().HaveCount(2);

        var late = await Act(financeClient, request.Id, "approve");
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _observer.Changes.Should().Equal("submitted", "step-approved", "approved");
    }

    [Fact]
    public async Task The_approvers_limit_must_cover_the_amount()
    {
        await ChainAsync("po", null, null, Step("Manager", "po:approve"));
        var request = await SubmitAsync(Guid.NewGuid(), 5000m);

        var capped = Guid.NewGuid();
        await LimitAsync(capped, "po:approve", 1000m);
        using var cappedClient = As(capped, "po:approve");
        var over = await Act(cappedClient, request.Id, "approve");
        over.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(over)).Should().StartWith("APPROVAL_LIMIT_EXCEEDED");

        using var noLimit = As(Guid.NewGuid(), "po:approve");
        (await CodeAsync(await Act(noLimit, request.Id, "approve"))).Should().StartWith("NO_APPROVAL_AUTHORITY");

        using var stranger = As(Guid.NewGuid());
        (await CodeAsync(await Act(stranger, request.Id, "approve"))).Should().StartWith("NOT_AN_APPROVER");

        // A step can opt out of the amount check.
        await ChainAsync("po2", null, null, Step("Review", "po:review", checkAuthority: false));
    }

    [Fact]
    public async Task A_step_that_needs_two_approvals_needs_two_different_people()
    {
        await ChainAsync("po", null, null, Step("Board", "po:board", approvals: 2, checkAuthority: false));
        var request = await SubmitAsync(Guid.NewGuid(), 100m);
        var first = Guid.NewGuid();
        using var firstClient = As(first, "po:board");

        var after = await ViewAsync(await Act(firstClient, request.Id, "approve"));
        after.Status.Should().Be("Pending");
        after.CurrentStep.Should().Be(0);
        (await Act(firstClient, request.Id, "approve")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var second = As(Guid.NewGuid(), "po:board");
        (await ViewAsync(await Act(second, request.Id, "approve"))).Status.Should().Be("Approved");
        _observer.Changes.Should().Equal("submitted", "approval-recorded", "approved");
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_ends_the_request_and_leaves_the_inbox()
    {
        await ChainAsync("po", null, null, Step("Manager", "po:approve", checkAuthority: false));
        var request = await SubmitAsync(Guid.NewGuid(), 100m);
        var manager = Guid.NewGuid();
        using var client = As(manager, "po:approve");

        (await client.GetFromJsonAsync<JsonElement[]>("/authorization/approvals/requests/inbox"))!.Should().ContainSingle();
        using var outsider = As(Guid.NewGuid());
        (await outsider.GetFromJsonAsync<JsonElement[]>("/authorization/approvals/requests/inbox"))!.Should().BeEmpty();

        (await Act(client, request.Id, "reject")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ViewAsync(await Act(client, request.Id, "reject", "wrong supplier"))).Status.Should().Be("Rejected");
        (await client.GetFromJsonAsync<JsonElement[]>("/authorization/approvals/requests/inbox"))!.Should().BeEmpty();

        // A rejected document can be submitted again.
        (await SubmitAsync(Guid.NewGuid(), 100m)).Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Only_the_requester_can_withdraw_a_pending_request()
    {
        await ChainAsync("po", null, null, Step("Manager", "po:approve", checkAuthority: false));
        var requester = Guid.NewGuid();
        var request = await SubmitAsync(requester, 100m);

        using var other = As(Guid.NewGuid(), "po:approve");
        (await Act(other, request.Id, "cancel")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var mine = As(requester);
        (await ViewAsync(await Act(mine, request.Id, "cancel"))).Status.Should().Be("Cancelled");
        (await mine.GetFromJsonAsync<JsonElement[]>("/authorization/approvals/requests/mine"))!.Should().ContainSingle();
    }

    [Fact]
    public async Task Two_approvers_racing_for_one_step_cannot_both_win()
    {
        await ChainAsync("po", null, null, Step("Manager", "po:approve", checkAuthority: false));
        var request = await SubmitAsync(Guid.NewGuid(), 100m);
        using var a = As(Guid.NewGuid(), "po:approve");
        using var b = As(Guid.NewGuid(), "po:approve");

        var results = await Task.WhenAll(Act(a, request.Id, "approve"), Act(b, request.Id, "approve"));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    [Fact]
    public async Task An_overdue_step_escalates_and_the_escalation_permission_can_then_decide()
    {
        await ChainAsync("po", null, null, Step("Manager", "po:approve", escalateMinutes: 60, escalation: "po:director", checkAuthority: false));
        var request = await SubmitAsync(Guid.NewGuid(), 100m);
        using var director = As(Guid.NewGuid(), "po:director");

        (await Act(director, request.Id, "approve")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _clock.Advance(TimeSpan.FromMinutes(30));
        (await EscalateAsync()).Should().Be(0);
        _clock.Advance(TimeSpan.FromMinutes(31));
        (await EscalateAsync()).Should().Be(1);
        (await EscalateAsync()).Should().Be(0, "a step escalates once");
        _observer.Changes.Should().Contain("escalated");

        using var inbox = As(Guid.NewGuid(), "po:director");
        (await inbox.GetFromJsonAsync<JsonElement[]>("/authorization/approvals/requests/inbox"))!.Should().ContainSingle();
        (await ViewAsync(await Act(director, request.Id, "approve"))).Status.Should().Be("Approved");
    }

    private async Task<int> EscalateAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApprovalWorkflow>().EscalateOverdueAsync();
    }

    [Fact]
    public async Task Submitting_needs_a_matching_chain_and_refuses_a_second_pending_request_for_the_document()
    {
        using var client = As(Guid.NewGuid());
        var none = await client.PostAsJsonAsync("/test/submit", new SubmitBody("PurchaseOrder", "PO-9", 10m));
        none.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await ChainAsync("small", null, 100m, Step("Manager", "po:approve"));
        (await client.PostAsJsonAsync("/test/submit", new SubmitBody("PurchaseOrder", "PO-9", 500m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/test/submit", new SubmitBody("PurchaseOrder", "PO-9", 50m))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/test/submit", new SubmitBody("PurchaseOrder", "PO-9", 50m))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<JsonElement[]>("/authorization/approvals/requests/document/PurchaseOrder/PO-9"))!.Should().ContainSingle();
    }

    [Fact]
    public async Task Chains_are_edited_only_by_administrators_and_a_bad_chain_is_refused()
    {
        using var plain = As(Guid.NewGuid());
        (await plain.GetAsync("/authorization/approvals/chains")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var admin = Admin();
        async Task<HttpStatusCode> Put(object body) => (await admin.PutAsJsonAsync("/authorization/approvals/chains", body)).StatusCode;

        (await Put(new { name = "x", documentType = "PurchaseOrder", steps = Array.Empty<object>() })).Should().Be(HttpStatusCode.BadRequest);
        (await Put(new { name = "x", documentType = "PurchaseOrder", steps = new[] { Step("A", "authorization:manage") } })).Should().Be(HttpStatusCode.BadRequest);
        (await Put(new { name = "x", documentType = "PurchaseOrder", steps = new[] { Step("A", "po:*") } })).Should().Be(HttpStatusCode.BadRequest);
        (await Put(new { name = "x", documentType = "PurchaseOrder", steps = new[] { Step("A", "po:approve", escalateMinutes: 30) } })).Should().Be(HttpStatusCode.BadRequest);
        (await Put(new { name = "x", documentType = "PurchaseOrder", minAmount = 10, maxAmount = 1, steps = new[] { Step("A", "po:approve") } })).Should().Be(HttpStatusCode.BadRequest);
        (await admin.DeleteAsync($"/authorization/approvals/chains/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var id = await ChainAsync("ok", null, null, Step("A", "po:approve"));
        (await admin.GetFromJsonAsync<JsonElement[]>("/authorization/approvals/chains"))!.Should().ContainSingle().Which.GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task A_request_is_visible_only_to_people_involved_and_a_decision_explains_itself()
    {
        await ChainAsync("po", null, null, Step("Manager", "po:approve", checkAuthority: false));
        var requester = Guid.NewGuid();
        var request = await SubmitAsync(requester, 100m);

        using var stranger = As(Guid.NewGuid());
        (await stranger.GetAsync($"/authorization/approvals/requests/{request.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var mine = As(requester);
        (await mine.GetAsync($"/authorization/approvals/requests/{request.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var why = await mine.GetFromJsonAsync<JsonElement>($"/authorization/approvals/requests/{request.Id}/why-not");
        why.GetProperty("canDecide").GetBoolean().Should().BeFalse();
        why.GetProperty("code").GetString().Should().Be("REQUESTER_CANNOT_APPROVE");

        using var manager = As(Guid.NewGuid(), "po:approve");
        (await manager.GetFromJsonAsync<JsonElement>($"/authorization/approvals/requests/{request.Id}/why-not")).GetProperty("canDecide").GetBoolean().Should().BeTrue();
    }
}
