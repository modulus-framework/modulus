using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Scopes;
using Modulus.BackgroundJobs;
using Modulus.BackgroundJobs.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>
/// TEST-2 (jobs over HTTP): a request queues a background job, and the job runs later as the person who queued it. The job's permission
/// step goes through <see cref="IJobStageGuard"/>, and its bulk step through the same scope helpers as the synchronous endpoint.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobsOverHttpTests : IAsyncLifetime
{
    private static readonly Guid Bob = JobsHttpHost.Bob;
    private static readonly Guid Alice = JobsHttpHost.Alice;

    private JobsHttpHost _host = null!;

    public async Task InitializeAsync() => _host = await JobsHttpHost.StartAsync(bindInitiator: true);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task An_approval_queued_over_HTTP_is_carried_out_by_the_job_for_its_own_scope()
    {
        _host.Grant(Bob, "orders:approve", PermissionScope.Own);
        var bobs = _host.Orders.Single(o => o.OwnerId == Bob);

        var response = await _host.As(Bob).PostAsJsonAsync("/orders/approve-later", new[] { bobs.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var outcome = await _host.NextOutcomeAsync();
        outcome.Outcome.Should().Be("approved", $"the job reported {outcome.Outcome}");
        outcome.Ids.Should().Equal(bobs.Id);
        bobs.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task An_approval_of_an_order_outside_the_callers_scope_is_refused_at_enqueue_and_never_runs()
    {
        _host.Grant(Bob, "orders:approve", PermissionScope.Own);
        var alices = _host.Orders.Single(o => o.OwnerId == Alice);
        var bobs = _host.Orders.Single(o => o.OwnerId == Bob);

        var refused = await _host.As(Bob).PostAsJsonAsync("/orders/approve-later", new[] { alices.Id });
        var accepted = await _host.As(Bob).PostAsJsonAsync("/orders/approve-later", new[] { bobs.Id });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the scope check runs before anything is queued");
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await _host.NextOutcomeAsync()).Ids.Should().NotContain(alices.Id);
        await Task.Delay(200);
        _host.Ledger.Outcomes.Should().BeEmpty("the refused approval never reached the queue");
        alices.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task A_job_whose_queuer_no_longer_holds_the_permission_is_refused_at_its_step()
    {
        _host.Grant(Bob, "orders:approve", PermissionScope.Own);
        _host.Initiators.Revoke(Bob);
        var bobs = _host.Orders.Single(o => o.OwnerId == Bob);

        var response = await _host.As(Bob).PostAsJsonAsync("/orders/approve-later", new[] { bobs.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the request itself is allowed; the check happens when the job runs");
        (await _host.NextOutcomeAsync()).Outcome.Should().Be("refused-permission");
        bobs.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task A_role_grant_reaches_the_job_through_the_roles_the_identity_store_gives_when_it_starts()
    {
        _host.GrantToRole("Buyers", "orders:approve", PermissionScope.Own);
        _host.Roles.Set(Bob, "Buyers");
        var bobs = _host.Orders.Single(o => o.OwnerId == Bob);

        var response = await _host.As(Bob, "Buyers").PostAsJsonAsync("/orders/approve-later", new[] { bobs.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await _host.NextOutcomeAsync()).Outcome.Should().Be("approved");
        bobs.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task A_role_the_identity_store_no_longer_gives_does_not_reach_the_job_even_if_the_request_carried_it()
    {
        _host.GrantToRole("Buyers", "orders:approve", PermissionScope.Own);
        _host.Roles.Set(Bob);
        var bobs = _host.Orders.Single(o => o.OwnerId == Bob);

        var response = await _host.As(Bob, "Buyers").PostAsJsonAsync("/orders/approve-later", new[] { bobs.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the request carried the role when it queued the job");
        (await _host.NextOutcomeAsync()).Outcome.Should().Be("refused-scope", "the job reads roles from the identity store when it starts");
        bobs.Approved.Should().BeFalse();
    }
}

/// <summary>
/// The gap the job host closes by hand: a job has no request, so its scope checks read the caller from <see cref="JobContext"/>. An app that
/// does not supply that identity gets a fail-closed result, not a silent company-wide one.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobsWithoutInitiatorIdentityTests : IAsyncLifetime
{
    private static readonly Guid Bob = JobsHttpHost.Bob;

    private JobsHttpHost _host = null!;

    public async Task InitializeAsync() => _host = await JobsHttpHost.StartAsync(bindInitiator: false);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Without_the_queuers_identity_a_job_approves_nothing_even_in_scope()
    {
        _host.Grant(Bob, "orders:approve", PermissionScope.Own);
        var bobs = _host.Orders.Single(o => o.OwnerId == Bob);

        var response = await _host.As(Bob).PostAsJsonAsync("/orders/approve-later", new[] { bobs.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await _host.NextOutcomeAsync()).Outcome.Should().Be("refused-scope");
        bobs.Approved.Should().BeFalse();
    }
}

public sealed record JobOutcome(string Outcome, Guid[] Ids);

public sealed record ApproveOrdersArgs(Guid[] Ids);

public sealed class QueuedOrder : IHasOwner, IHasOrgUnit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OwnerId { get; init; }
    public Guid OrgUnitId { get; init; }
    public bool Approved { get; set; }
}

/// <summary>Records what each job did, so a test can wait for the job's outcome.</summary>
public sealed class JobLedger
{
    public ConcurrentQueue<JobOutcome> Outcomes { get; } = new();

    public void Record(string outcome, Guid[] ids) => Outcomes.Enqueue(new JobOutcome(outcome, ids));
}

/// <summary>A test guard that lets a revoked queuer's job be refused at its permission step.</summary>
public sealed class RevocableInitiators : IJobInitiatorGuard
{
    private readonly ConcurrentDictionary<Guid, byte> _revoked = new();

    public void Revoke(Guid userId) => _revoked[userId] = 0;

    public Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct) => Task.FromResult(true);

    public Task<bool> MayPerformAsync(Guid initiatorId, string permission, CancellationToken ct)
        => Task.FromResult(!_revoked.ContainsKey(initiatorId));
}

public sealed class ApproveOrdersJob(JobsOrderStore store, IJobStageGuard stage, IScopeEnforcer scopes, JobLedger ledger)
    : IBackgroundJob<ApproveOrdersArgs>
{
    public async Task ExecuteAsync(ApproveOrdersArgs args, CancellationToken ct)
    {
        try
        {
            await stage.EnsureAllowedAsync("orders:approve", ct);
        }
        catch (ForbiddenException)
        {
            ledger.Record("refused-permission", args.Ids);
            return;
        }

        var batch = store.Items.Where(o => args.Ids.Contains(o.Id)).ToList();
        try
        {
            scopes.EnsureAllInScope(batch, "orders:approve");
        }
        catch (ForbiddenException)
        {
            ledger.Record("refused-scope", args.Ids);
            return;
        }

        foreach (var order in batch)
            order.Approved = true;
        ledger.Record("approved", args.Ids);
    }
}

public sealed class JobsOrderStore
{
    public List<QueuedOrder> Items { get; } = [];
}

/// <summary>
/// The caller a request or a job acts as. In a request it is the signed-in user. In a job it is the person who queued the job, when the
/// host binds one (<c>bindInitiator</c>); otherwise a job has no caller at all.
/// </summary>
public sealed class JobCaller(IHttpContextAccessor accessor, JobContext job, bool bindInitiator) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var fromRequest = accessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(fromRequest, out var id))
                return id;
            return bindInitiator ? job.InitiatorId : null;
        }
    }

    public string? UserName => null;
    public string? Email => null;
    public bool IsAuthenticated => UserId is not null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string permission) => false;
    public IReadOnlyList<string> Permissions => [];
}

/// <summary>The identity store a job reads roles from when it starts. An unknown user has none.</summary>
public sealed class TestRoleDirectory : IUserRoleDirectory
{
    private readonly ConcurrentDictionary<Guid, string[]> _roles = new();

    public void Set(Guid userId, params string[] roles) => _roles[userId] = roles;

    public ValueTask<IReadOnlyCollection<string>?> GetRolesAsync(Guid userId, CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyCollection<string>?>(_roles.TryGetValue(userId, out var roles) ? roles : null);
}

public sealed class JobsHeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-UserId", out var userId))
            return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (Request.Headers.TryGetValue("X-Test-Roles", out var roles))
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}

/// <summary>A host with a running job queue, an approval endpoint that queues the job, and scoped orders.</summary>
public sealed class JobsHttpHost : IAsyncDisposable
{
    private static readonly Guid s_sales = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid s_finance = Guid.Parse("22222222-0000-0000-0000-000000000002");

    public static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private WebApplication _app = null!;
    private InMemoryPermissionGrantStore _grants = null!;

    public JobsOrderStore Store { get; private set; } = null!;
    public RevocableInitiators Initiators { get; private set; } = null!;
    public JobLedger Ledger { get; private set; } = null!;
    public TestRoleDirectory Roles { get; private set; } = null!;

    public List<QueuedOrder> Orders => Store.Items;

    public static async Task<JobsHttpHost> StartAsync(bool bindInitiator)
    {
        var host = new JobsHttpHost();
        await host.StartCoreAsync(bindInitiator);
        return host;
    }

    public void Grant(Guid user, string permission, PermissionScope scope)
        => _grants.Add(new PermissionGrant(GrantHolderType.User, user.ToString(), permission, PermissionGrantType.Allow, scope));

    public void GrantToRole(string role, string permission, PermissionScope scope)
        => _grants.Add(new PermissionGrant(GrantHolderType.Role, role, permission, PermissionGrantType.Allow, scope));

    /// <summary>A client signed in as <paramref name="user"/>, whose request carries the given role claims.</summary>
    public HttpClient As(Guid user, params string[] roles)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", user.ToString());
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', roles));
        return client;
    }

    /// <summary>Waits for the next job outcome; fails if no job finishes in time.</summary>
    public async Task<JobOutcome> NextOutcomeAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            if (Ledger.Outcomes.TryDequeue(out var outcome))
                return outcome;
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException("No background job finished within 10 seconds.");
            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    private async Task StartCoreAsync(bool bindInitiator)
    {
        // Development turns on scope validation, which is where a scoped caller resolved from the wrong provider throws.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, JobsHeaderAuthHandler>("Test", null);
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser>(sp => new JobCaller(
            sp.GetRequiredService<IHttpContextAccessor>(), sp.GetRequiredService<JobContext>(), bindInitiator));
        builder.Services.AddSingleton<ICurrentDataScope>(new NoDataScope());
        builder.Services.AddSingleton<TestRoleDirectory>();
        builder.Services.AddSingleton<IUserRoleDirectory>(sp => sp.GetRequiredService<TestRoleDirectory>());
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = false);
        builder.Services.AddOrganization();
        builder.Services.AddPermissions("Orders", registry =>
        {
            registry.Add("orders:approve", "Approve orders.");
        });
        builder.Services.AddScopeMap<QueuedOrder>(m => m.Owner(o => o.OwnerId).OrgUnit(o => o.OrgUnitId));
        builder.Services.AddSingleton<JobsOrderStore>();
        builder.Services.AddSingleton<JobLedger>();
        builder.Services.AddSingleton<RevocableInitiators>();
        builder.Services.AddScoped<IJobInitiatorGuard>(sp => sp.GetRequiredService<RevocableInitiators>());
        builder.Services.AddBackgroundJobs(typeof(JobsOverHttpTests).Assembly);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _grants = _app.Services.GetRequiredService<InMemoryPermissionGrantStore>();
        Store = _app.Services.GetRequiredService<JobsOrderStore>();
        Initiators = _app.Services.GetRequiredService<RevocableInitiators>();
        Ledger = _app.Services.GetRequiredService<JobLedger>();
        Roles = _app.Services.GetRequiredService<TestRoleDirectory>();
        var scheduler = _app.Services.GetRequiredService<IJobScheduler>();

        Store.Items.AddRange(
        [
            new QueuedOrder { OwnerId = Alice, OrgUnitId = s_sales },
            new QueuedOrder { OwnerId = Bob, OrgUnitId = s_finance },
        ]);

        // The request checks the caller's scope and queues the job; the job does the approval later, as the queuer.
        _app.MapPost("/orders/approve-later", async (Guid[] ids, IScopeEnforcer scopes, JobsOrderStore store) =>
        {
            var batch = store.Items.Where(o => ids.Contains(o.Id)).ToList();
            try
            {
                scopes.EnsureAllInScope(batch, "orders:approve");
            }
            catch (ForbiddenException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await scheduler.EnqueueAsync<ApproveOrdersJob, ApproveOrdersArgs>(new ApproveOrdersArgs(ids));
            return Results.Accepted();
        }).RequireAuthorization();

        await _app.StartAsync();
    }

    private sealed class NoDataScope : ICurrentDataScope
    {
        public bool IsUnrestricted => false;
        public IReadOnlyCollection<Guid> OrgUnitIds => [];
    }
}
