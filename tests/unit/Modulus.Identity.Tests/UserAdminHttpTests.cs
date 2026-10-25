using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Authorization.Extensions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Premodule 1, the user administration API, over HTTP (pilot scenarios 2 and 3 in <c>docs/IDENTITY_PLATFORM_PLAN.md</c> §14.2).
/// An anonymous caller and a password-only caller are refused; an administrator with a second factor lists, reads, changes roles,
/// locks and unlocks, and every write is recorded in the security audit.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UserAdminHttpTests : OidcHostTestBase
{
    private const string AccountAdminRole = "AccountAdmin";
    private const string PersonalDataRole = "PersonalDataReader";
    private const string OrgReaderRole = "OrganizationReader";
    private const string OrgManagerRole = "OrganizationManager";
    private static readonly Guid KnownBranch = Guid.Parse("b7a1c2d3-0000-4000-8000-00000000be01");

    // The company's org units for these tests: one branch, which the primary-branch rule accepts.
    private sealed class KnownBranchHierarchy(Guid branch) : Modulus.Authorization.Organization.IOrgHierarchy
    {
        public bool Contains(Guid orgUnitId) => orgUnitId == branch;
        public IReadOnlySet<Guid> Descendants(Guid orgUnitId) => new HashSet<Guid>();
        public IReadOnlySet<Guid> Ancestors(Guid orgUnitId) => new HashSet<Guid>();
    }
    private readonly RecordingAuditLog _audit = new();
    private readonly SwitchableTenant _tenant = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(_tenant));
        services.AddSingleton<ISecurityAuditLog>(_audit);
        services.AddModulusAccountLifecycleAdmin();
        // The field guard asks ICurrentUser.HasPermission. The host's stand-in always grants grant-any, so the real claims-based user,
        // backed by the grant store, is used here, as in a generated host.
        services.AddModulusAuthorization();
        services.AddGrantStorePermissionChecker();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, ClaimsPrincipalCurrentUser>());
        services.AddSingleton<Modulus.Authorization.Organization.IOrgHierarchy>(new KnownBranchHierarchy(KnownBranch));
        services.AddPermissionGrants(grants => grants
            .GrantToRole(AccountAdminRole, AccountLifecycleExtensions.ManagePermission)
            .GrantToRole(PersonalDataRole, AccountLifecycleExtensions.PersonalDataPermission)
            .GrantToRole(OrgReaderRole, AccountLifecycleExtensions.OrganizationPermission)
            .GrantToRole(OrgManagerRole, AccountLifecycleExtensions.OrganizationManagePermission));
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        // Test stand-in for company resolution: a request names the company in X-Test-Company, and the company is in scope for the
        // request's async flow, which is where the identity query filter reads it. Membership is not tested here.
        app.Use(async (context, next) =>
        {
            var company = context.Request.Headers["X-Test-Company"].FirstOrDefault();
            using (_tenant.Change(company is null ? null : new TenantInfo(Guid.Parse(company), "test-company")))
                await next(context);
        });
        app.MapModulusUserAdmin<ModulusUser>();
        app.MapModulusAccountLifecycle();
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_on_every_route()
    {
        var target = await CreateUserAsync("target");

        (await CallAsync(HttpMethod.Get, "/identity/users", bearer: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CallAsync(HttpMethod.Get, $"/identity/users/{target.Id}", bearer: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Put, $"/identity/users/{target.Id}/roles", null, new { roles = new[] { "Auditor" } })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Post, $"/identity/users/{target.Id}/lock", null, new { until = (DateTimeOffset?)null, reason = "x" })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        _audit.Events.Should().BeEmpty("nothing was changed");
    }

    [Fact]
    public async Task A_password_only_administrator_is_stepped_up_before_the_list_is_read()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await CreateUserAsync("admin", AccountAdminRole);
        var (status, body) = await PasswordAsync("admin");
        status.Should().Be(HttpStatusCode.OK);

        var response = await CallAsync(HttpMethod.Get, "/identity/users", body.GetProperty("access_token").GetString());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("insufficient_user_authentication");
    }

    [Fact]
    public async Task An_administrator_with_a_second_factor_manages_accounts_and_every_write_is_audited()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync("Auditor");
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var target = await CreateUserAsync("target");
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var list = await SendAsync(HttpMethod.Get, "/identity/users?search=target", token, null);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await list.Content.ReadFromJsonAsync<JsonElement>();
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("userName").GetString()).Should().Contain("target");

        var detail = await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}", token, null);
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roles").GetArrayLength().Should().Be(0);

        var roles = await SendAsync(HttpMethod.Put, $"/identity/users/{target.Id}/roles", token, new { roles = new[] { "Auditor" } });
        roles.StatusCode.Should().Be(HttpStatusCode.OK);
        (await roles.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("Auditor");

        var refused = await SendAsync(HttpMethod.Put, $"/identity/users/{target.Id}/roles", token, new { roles = new[] { "integration:svc" } });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, "integration roles belong to machine clients");

        var until = DateTimeOffset.UtcNow.AddHours(2);
        var locked = await SendAsync(HttpMethod.Post, $"/identity/users/{target.Id}/lock", token, new { until, reason = "Suspected misuse" });
        locked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lockedUntil").ValueKind.Should().NotBe(JsonValueKind.Null);

        var noReason = await SendAsync(HttpMethod.Post, $"/identity/users/{target.Id}/lock", token, new { until = (DateTimeOffset?)null, reason = " " });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a lock needs a reason");

        var unlocked = await SendAsync(HttpMethod.Post, $"/identity/users/{target.Id}/unlock", token, null);
        unlocked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await unlocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lockedUntil").ValueKind.Should().Be(JsonValueKind.Null);

        var missing = await SendAsync(HttpMethod.Get, $"/identity/users/{Guid.NewGuid()}", token, null);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var writes = _audit.Events.Where(e => e.Action.StartsWith("identity.user.", StringComparison.Ordinal)).ToList();
        writes.Select(e => e.Action).Should().Equal("identity.user.roles.changed", "identity.user.locked", "identity.user.unlocked");
        writes.All(e => e.Actor == admin.Id.ToString("D") && e.Target == $"user:{target.Id:D}").Should().BeTrue("each event names the actor and the target");
    }

    [Fact]
    public async Task The_last_active_tenant_owner_cannot_be_demoted_and_ownership_changes_are_audited()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync(AccountLifecycleExtensions.TenantOwnerRole);
        var owner = await CreateUserAsync("owner", AccountAdminRole, AccountLifecycleExtensions.TenantOwnerRole);
        var successor = await CreateUserAsync("successor");
        var key = await EnableSecondFactorAsync(owner);
        var (status, body) = await PasswordAsync("owner", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var selfDemotion = await SendAsync(HttpMethod.Put, $"/identity/users/{owner.Id}/roles", token, new { roles = new[] { AccountAdminRole } });
        selfDemotion.StatusCode.Should().Be(HttpStatusCode.Conflict, "the last active owner cannot be demoted");
        (await selfDemotion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("LAST_TENANT_OWNER");

        var promoted = await SendAsync(HttpMethod.Put, $"/identity/users/{successor.Id}/roles", token, new { roles = new[] { AccountLifecycleExtensions.TenantOwnerRole } });
        promoted.StatusCode.Should().Be(HttpStatusCode.OK, "an owner names another owner");

        var demoted = await SendAsync(HttpMethod.Put, $"/identity/users/{owner.Id}/roles", token, new { roles = new[] { AccountAdminRole } });
        demoted.StatusCode.Should().Be(HttpStatusCode.OK, "another active owner exists");

        var ownership = _audit.Events.Where(e => e.Action.StartsWith("identity.tenant-owner.", StringComparison.Ordinal)).Select(e => e.Action).ToList();
        ownership.Should().Equal("identity.tenant-owner.granted", "identity.tenant-owner.revoked");
    }

    [Fact]
    public async Task The_last_active_owner_cannot_be_locked_or_disabled_until_another_owner_exists()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync(AccountLifecycleExtensions.TenantOwnerRole);
        var owner = await CreateUserAsync("owner", AccountAdminRole, AccountLifecycleExtensions.TenantOwnerRole);
        var successor = await CreateUserAsync("successor");
        var key = await EnableSecondFactorAsync(owner);
        var (status, body) = await PasswordAsync("owner", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var locked = await SendAsync(HttpMethod.Post, $"/identity/users/{owner.Id}/lock", token, new { until = (DateTimeOffset?)null, reason = "Review" });
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("LAST_TENANT_OWNER");

        var disabled = await SendAsync(HttpMethod.Put, $"/identity/accounts/{owner.Id}/lifecycle", token, new { To = (int)AccountLifecycle.Disabled, Reason = "other", Note = "x" });
        disabled.StatusCode.Should().Be(HttpStatusCode.Conflict, "disabling the last owner is refused too");

        (await SendAsync(HttpMethod.Put, $"/identity/users/{successor.Id}/roles", token, new { roles = new[] { AccountLifecycleExtensions.TenantOwnerRole } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var lockedAfter = await SendAsync(HttpMethod.Post, $"/identity/users/{owner.Id}/lock", token, new { until = (DateTimeOffset?)null, reason = "Review" });
        lockedAfter.StatusCode.Should().Be(HttpStatusCode.OK, "another active owner exists");
    }

    [Fact]
    public async Task Organization_details_need_the_organization_clearance_even_for_an_administrator()
    {
        var target = await CreateUserAsync("target");
        using (var scope = _app.Services.CreateScope())
        {
            using (_tenant.Change(null))
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
                var fresh = await users.FindByIdAsync(target.Id.ToString());
                fresh!.JobTitle = "Analyst";
                fresh.Department = "Operations";
                (await users.UpdateAsync(fresh)).Succeeded.Should().BeTrue();
            }
        }

        // An administrator with the account and personal-data permissions, but no organization clearance: the organization fields are masked.
        var plainAdmin = await SecondFactorTokenAsync("plain-admin", AccountAdminRole, PersonalDataRole);
        var masked = await (await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}", plainAdmin, null)).Content.ReadFromJsonAsync<JsonElement>();
        masked.GetProperty("jobTitle").ValueKind.Should().Be(JsonValueKind.Null, "holding the admin role does not clear Confidential data");
        masked.GetProperty("department").ValueKind.Should().Be(JsonValueKind.Null);

        // The same administrator with the organization clearance sees them.
        var cleared = await SecondFactorTokenAsync("cleared-admin", AccountAdminRole, PersonalDataRole, OrgReaderRole);
        var visible = await (await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}", cleared, null)).Content.ReadFromJsonAsync<JsonElement>();
        visible.GetProperty("jobTitle").GetString().Should().Be("Analyst");
        visible.GetProperty("department").GetString().Should().Be("Operations");

        // An ordinary account without the administration permission cannot reach the detail at all.
        var ordinary = await SecondFactorTokenAsync("ordinary");
        (await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}", ordinary, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Changing_the_employee_code_needs_the_organization_manage_permission_not_only_account_administration()
    {
        var target = await CreateUserAsync("target");
        var accountOnly = await SecondFactorTokenAsync("account-admin", AccountAdminRole, PersonalDataRole);
        var refused = await SendAsync(HttpMethod.Patch, $"/identity/users/{target.Id}/organization", accountOnly, new { employeeCode = "EMP-1" });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "account administration alone does not reassign people");

        var manager = await SecondFactorTokenAsync("org-manager", AccountAdminRole, OrgManagerRole);
        var allowed = await SendAsync(HttpMethod.Patch, $"/identity/users/{target.Id}/organization", manager, new { employeeCode = "EMP-1" });
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_administrator_enters_a_work_email_only_as_a_pending_one_and_needs_the_organization_manage_permission()
    {
        var target = await CreateUserAsync("work-target");
        var accountOnly = await SecondFactorTokenAsync("work-account-admin", AccountAdminRole, PersonalDataRole);
        var refused = await SendAsync(HttpMethod.Patch, $"/identity/users/{target.Id}/work-email", accountOnly, new { workEmail = "target@work.example" });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "account administration alone does not set organization data");

        var manager = await SecondFactorTokenAsync("work-org-manager", AccountAdminRole, OrgManagerRole);
        var accepted = await SendAsync(HttpMethod.Patch, $"/identity/users/{target.Id}/work-email", manager, new { workEmail = "target@work.example" });
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Entered by an administrator, it is not verified: the stored work e-mail stays empty and the address is pending.
        using var scope = _app.Services.CreateScope();
        using var host = _tenant.Change(null);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var stored = await users.FindByIdAsync(target.Id.ToString());
        stored!.WorkEmail.Should().BeNull("an administrator's entry is not verified by being entered");
        stored.PendingWorkEmail.Should().Be("target@work.example");
        _audit.Events.Should().Contain(e => e.Action == "identity.user.work-email.requested");
    }

    private async Task<string> SecondFactorTokenAsync(string name, params string[] roles)
    {
        foreach (var role in roles)
            await EnsureRoleAsync(role);
        var user = await CreateUserAsync(name, roles);
        var key = await EnableSecondFactorAsync(user);
        var (status, body) = await PasswordAsync(name, mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        return body.GetProperty("access_token").GetString()!;
    }

    [Fact]
    public async Task An_administrator_sets_the_employee_code_and_the_primary_branch_and_a_duplicate_code_is_refused()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync(PersonalDataRole);
        await EnsureRoleAsync(OrgManagerRole);
        // The personal-data permission is needed to read the employee code (a Restricted field); the organization-manage permission to set it.
        var admin = await CreateUserAsync("admin", AccountAdminRole, PersonalDataRole, OrgManagerRole);
        var first = await CreateUserAsync("first");
        var second = await CreateUserAsync("second");
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();
        var branch = KnownBranch;

        var foreign = await SendAsync(HttpMethod.Patch, $"/identity/users/{first.Id}/organization", token, new { primaryBranchId = Guid.NewGuid() });
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a branch outside the company's org units is refused");

        var set = await SendAsync(HttpMethod.Patch, $"/identity/users/{first.Id}/organization", token, new { employeeCode = "emp-0042", primaryBranchId = branch });
        set.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await set.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("employeeCode").GetString().Should().Be("EMP-0042", "codes are stored upper-case");
        detail.GetProperty("primaryBranchId").GetGuid().Should().Be(branch);

        var duplicate = await SendAsync(HttpMethod.Patch, $"/identity/users/{second.Id}/organization", token, new { employeeCode = "EMP-0042" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("EMPLOYEE_CODE_TAKEN");

        var bad = await SendAsync(HttpMethod.Patch, $"/identity/users/{second.Id}/organization", token, new { employeeCode = "emp code!" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var cleared = await SendAsync(HttpMethod.Patch, $"/identity/users/{first.Id}/organization", token, new { clearPrimaryBranch = true });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("primaryBranchId").ValueKind.Should().Be(JsonValueKind.Null);

        _audit.Events.Count(e => e.Action == "identity.user.organization.changed").Should().Be(2, "each successful change is audited; the refusals are not");
    }

    [Fact]
    public async Task A_normal_administrator_cannot_grant_the_tenant_owner_role()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync(AccountLifecycleExtensions.TenantOwnerRole);
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var target = await CreateUserAsync("target");
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var refused = await SendAsync(HttpMethod.Put, $"/identity/users/{target.Id}/roles", token, new { roles = new[] { AccountLifecycleExtensions.TenantOwnerRole } });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("TENANT_OWNER_REQUIRED");
        _audit.Events.Should().NotContain(e => e.Action.StartsWith("identity.tenant-owner.", StringComparison.Ordinal), "nothing changed");
    }

    [Fact]
    public async Task Personal_fields_are_masked_without_the_personal_data_permission_and_shown_with_it()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync(PersonalDataRole);
        var target = await CreateUserAsync("target");
        var plain = await CreateUserAsync("plain", AccountAdminRole);
        var plainKey = await EnableSecondFactorAsync(plain);
        var (plainStatus, plainBody) = await PasswordAsync("plain", mfa: Totp(plainKey));
        plainStatus.Should().Be(HttpStatusCode.OK);
        var plainToken = plainBody.GetProperty("access_token").GetString();

        var masked = await (await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}", plainToken, null)).Content.ReadFromJsonAsync<JsonElement>();
        masked.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null, "the address is personal data");
        masked.GetProperty("firstName").ValueKind.Should().Be(JsonValueKind.Null);
        masked.GetProperty("userName").GetString().Should().Be("target", "the user name is not personal data");

        var byAddress = await (await SendAsync(HttpMethod.Get, "/identity/users?search=target@example.test", plainToken, null)).Content.ReadFromJsonAsync<JsonElement>();
        byAddress.GetProperty("total").GetInt32().Should().Be(0, "a caller who cannot read addresses cannot search by them");

        var reader = await CreateUserAsync("reader", AccountAdminRole, PersonalDataRole);
        var readerKey = await EnableSecondFactorAsync(reader);
        var (readerStatus, readerBody) = await PasswordAsync("reader", mfa: Totp(readerKey));
        readerStatus.Should().Be(HttpStatusCode.OK);
        var readerToken = readerBody.GetProperty("access_token").GetString();

        var shown = await (await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}", readerToken, null)).Content.ReadFromJsonAsync<JsonElement>();
        shown.GetProperty("email").GetString().Should().Be("target@example.test");
        var found = await (await SendAsync(HttpMethod.Get, "/identity/users?search=target@example.test", readerToken, null)).Content.ReadFromJsonAsync<JsonElement>();
        found.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task An_administrator_invites_a_person_who_has_no_password_yet_and_the_invitation_is_refused_for_bad_input()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync("Visitor");
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        // Handing out a role hands out its permissions, so inviting into one needs authorization:grant-any; this administrator lacks it.
        var refusedRole = await SendAsync(HttpMethod.Post, "/identity/users/invitations", token,
            new { email = "newcomer@example.test", roles = new[] { "Visitor" }, firstName = "New" });
        refusedRole.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refusedRole.Content.ReadAsStringAsync()).Should().Contain("authorization:grant-any");

        var invited = await SendAsync(HttpMethod.Post, "/identity/users/invitations", token,
            new { email = "newcomer@example.test", roles = Array.Empty<string>(), firstName = "New" });
        invited.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitedId = (await invited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _audit.Events.Should().Contain(e => e.Action == "identity.user.invited" && e.Target == $"user:{invitedId:D}");

        var detail = await (await SendAsync(HttpMethod.Get, $"/identity/users/{invitedId}", token, null)).Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("lifecycle").GetString().Should().Be("PendingActivation");
        detail.GetProperty("roles").EnumerateArray().Should().BeEmpty();

        (await SendAsync(HttpMethod.Post, "/identity/users/invitations", token, new { email = "not-an-address", roles = Array.Empty<string>() })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, "/identity/users/invitations", token, new { email = "admin@example.test", roles = Array.Empty<string>() })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "an account that already has a password is not invited again");
    }

    [Fact]
    public async Task The_user_list_filters_by_status_and_by_role()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync(PersonalDataRole);
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();
        var inactive = await CreateUserAsync("filter-inactive");
        var locked = await CreateUserAsync("filter-locked");
        var member = await CreateUserAsync("filter-member", PersonalDataRole);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var stored = await users.FindByIdAsync(inactive.Id.ToString());
            stored!.IsActive = false;
            (await users.UpdateAsync(stored)).Succeeded.Should().BeTrue();
            var lockedStored = (await users.FindByIdAsync(locked.Id.ToString()))!;
            (await users.SetLockoutEndDateAsync(lockedStored, DateTimeOffset.UtcNow.AddHours(1))).Succeeded.Should().BeTrue();
        }

        async Task<List<string?>> NamesAsync(string query)
        {
            var response = await SendAsync(HttpMethod.Get, $"/identity/users{query}", token, null);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{query}: {text}");
            var page = JsonSerializer.Deserialize<JsonElement>(text);
            return page.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ToList();
        }

        (await NamesAsync("?status=inactive")).Should().Equal("filter-inactive");
        (await NamesAsync("?status=locked")).Should().Equal("filter-locked");
        (await NamesAsync("?status=active&search=filter-")).Should().Contain("filter-member").And.NotContain(new[] { "filter-inactive", "filter-locked" });
        (await NamesAsync($"?role={PersonalDataRole}")).Should().Equal("filter-member");
        (await SendAsync(HttpMethod.Get, "/identity/users?status=dormant", token, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_administrator_reads_an_accounts_sessions_and_login_history_and_ends_one_session()
    {
        await EnsureRoleAsync(AccountAdminRole);
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();
        var target = await CreateUserAsync("session-target");

        var sessions = await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}/sessions", token, null);
        sessions.StatusCode.Should().Be(HttpStatusCode.OK, await sessions.Content.ReadAsStringAsync());
        var history = await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}/login-history", token, null);
        history.StatusCode.Should().Be(HttpStatusCode.OK, await history.Content.ReadAsStringAsync());
        (await SendAsync(HttpMethod.Get, $"/identity/users/{Guid.NewGuid()}/sessions", token, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Delete, $"/identity/users/{target.Id}/sessions/no-such-session", token, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_export_masks_personal_fields_for_a_caller_without_the_clearance_and_is_audited_by_row_count()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync(PersonalDataRole);
        await CreateUserAsync("export-subject");
        var plain = await CreateUserAsync("export-plain", AccountAdminRole);
        var plainKey = await EnableSecondFactorAsync(plain);
        var (plainStatus, plainBody) = await PasswordAsync("export-plain", mfa: Totp(plainKey));
        plainStatus.Should().Be(HttpStatusCode.OK);
        var plainToken = plainBody.GetProperty("access_token").GetString();
        var reader = await CreateUserAsync("export-reader", AccountAdminRole, PersonalDataRole);
        var readerKey = await EnableSecondFactorAsync(reader);
        var (readerStatus, readerBody) = await PasswordAsync("export-reader", mfa: Totp(readerKey));
        readerStatus.Should().Be(HttpStatusCode.OK);
        var readerToken = readerBody.GetProperty("access_token").GetString();

        var masked = await SendAsync(HttpMethod.Get, "/identity/users/export?search=export-subject", plainToken, null);
        masked.StatusCode.Should().Be(HttpStatusCode.OK);
        masked.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var maskedText = await masked.Content.ReadAsStringAsync();
        maskedText.Should().Contain("export-subject");
        maskedText.Should().NotContain("export-subject@example.test", "the address is personal data");

        var shown = await (await SendAsync(HttpMethod.Get, "/identity/users/export?search=export-subject", readerToken, null)).Content.ReadAsStringAsync();
        shown.Should().Contain("export-subject@example.test");
        _audit.Events.Should().Contain(e => e.Action == "identity.user.exported" && e.Actor == reader.Id.ToString("D") && e.Details["rows"] == "1");
    }

    [Fact]
    public async Task An_account_administrator_reads_what_an_account_can_do_through_its_roles()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync(PersonalDataRole);
        var target = await CreateUserAsync("target", PersonalDataRole);
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var response = await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}/effective-access", token, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        report.GetProperty("all").EnumerateArray().Select(p => p.GetString()).Should().Contain("identity:accounts:personal-data");
        (await SendAsync(HttpMethod.Get, $"/identity/users/{Guid.NewGuid()}/effective-access", token, null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Over_http_a_company_administrator_sees_and_changes_only_its_own_users()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await CreateUserAsync("admin", AccountAdminRole);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        await CreateCompanyUserAsync("ann", companyA);
        var bob = await CreateCompanyUserAsync("bob", companyB);
        using var admin = _app.Services.CreateScope();
        var key = await EnableSecondFactorAsync((await admin.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>().FindByNameAsync("admin"))!);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var list = await SendAsync(HttpMethod.Get, "/identity/users", token, null, company: companyA);
        var names = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("userName").GetString()).ToList();
        names.Should().Contain("ann").And.NotContain("bob");

        (await SendAsync(HttpMethod.Get, $"/identity/users/{bob.Id}", token, null, company: companyA)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, $"/identity/users/{bob.Id}/roles", token, new { roles = new[] { "Auditor" } }, company: companyA)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "another company's user cannot be changed");

        var hostView = await SendAsync(HttpMethod.Get, "/identity/users?search=bob", token, null);
        (await hostView.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt32().Should().Be(1, "the host sees every company");
    }

    private async Task<ModulusUser> CreateCompanyUserAsync(string name, Guid company)
    {
        using (_tenant.Change(null))
        {
            using var scope = _app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = new ModulusUser { UserName = name, Email = $"{name}@example.test", EmailConfirmed = true, TenantId = company };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            return user;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? bearer, object? body, Guid? company = null, string? ifMatch = null)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        if (company is { } id)
            request.Headers.Add("X-Test-Company", id.ToString("D"));
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_write_with_a_stale_ETag_is_refused_and_a_write_with_the_current_one_is_accepted()
    {
        await EnsureRoleAsync(AccountAdminRole);
        await EnsureRoleAsync("Auditor");
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var target = await CreateUserAsync("target");
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var read = await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}", token, null);
        var etag = read.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrEmpty("the detail carries the account's version");

        var stale = await SendAsync(HttpMethod.Put, $"/identity/users/{target.Id}/roles", token, new { roles = new[] { "Auditor" } }, ifMatch: "\"an-older-version\"");
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("STALE_WRITE");
        (await (await SendAsync(HttpMethod.Get, $"/identity/users/{target.Id}", token, null)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("roles").GetArrayLength().Should().Be(0, "a refused write changes nothing");

        var current = await SendAsync(HttpMethod.Put, $"/identity/users/{target.Id}/roles", token, new { roles = new[] { "Auditor" } }, ifMatch: etag);
        current.StatusCode.Should().Be(HttpStatusCode.OK);
        var newTag = current.Headers.ETag?.Tag;
        newTag.Should().NotBeNullOrEmpty().And.NotBe(etag, "a role change moves the version");

        var late = await SendAsync(HttpMethod.Post, $"/identity/users/{target.Id}/lock", token, new { until = (DateTimeOffset?)null, reason = "Late write" }, ifMatch: etag);
        late.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, "the tag from before the role change is stale");

        var fresh = await SendAsync(HttpMethod.Post, $"/identity/users/{target.Id}/lock", token, new { until = (DateTimeOffset?)null, reason = "Fresh write" }, ifMatch: newTag);
        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> EnableSecondFactorAsync(ModulusUser user)
    {
        using var scope = _app.Services.CreateScope();
        var twoFactor = scope.ServiceProvider.GetRequiredService<IUserTwoFactorService>();
        var setup = (await twoFactor.BeginSetupAsync(user.Id))!;
        var key = setup.SharedKey.Replace(" ", "", StringComparison.Ordinal);
        (await twoFactor.EnableAsync(user.Id, Totp(key))).Should().NotBeNull();
        return key;
    }

    private async Task EnsureRoleAsync(string role)
    {
        using var scope = _app.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();
        if (!await roles.RoleExistsAsync(role))
            (await roles.CreateAsync(new ModulusRole { Name = role })).Succeeded.Should().BeTrue();
    }

    // RFC 6238 (HMAC-SHA1, 30 s, 6 digits) from the base32 key, the algorithm authenticator apps use. Same as the two-factor tests.
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32Key.ToUpperInvariant().Where(alphabet.Contains).Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        var hash = System.Security.Cryptography.HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0xF;
        var code = (((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3]) % 1_000_000;
        return code.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class RecordingAuditLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
