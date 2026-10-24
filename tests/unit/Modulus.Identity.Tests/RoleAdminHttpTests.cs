using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Premodule 1: the permission catalog (read-only) and the role list and create, over HTTP. Anonymous callers and password-only callers are
/// refused; an administrator with a second factor reads the catalog, lists roles, creates a role, and is refused the reserved and duplicate names.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RoleAdminHttpTests : OidcHostTestBase
{
    private const string AccountAdminRole = "AccountAdmin";
    private const string GrantedRole = "Granted";
    private readonly RecordingAudit _audit = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISecurityAuditLog>(_audit);
        services.AddModulusAccountLifecycleAdmin();
        services.AddPermissionGrants(grants => grants.GrantToRole(AccountAdminRole, AccountLifecycleExtensions.ManagePermission));
        services.AddSingleton<IRoleDeletionGuard>(new GrantedRoleGuard(GrantedRole));
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapModulusPermissionCatalog();
        app.MapModulusRoleAdmin<ModulusRole>();
        app.MapModulusRoleDelete<ModulusRole, ModulusUser>();
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_on_the_catalog_and_the_roles()
    {
        (await CallAsync(HttpMethod.Get, "/api/identity/permissions", bearer: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CallAsync(HttpMethod.Get, "/api/identity/roles", bearer: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_administrator_with_a_second_factor_reads_the_catalog_and_manages_role_names()
    {
        await EnsureRoleAsync(AccountAdminRole);
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var catalog = await SendAsync(HttpMethod.Get, "/api/identity/permissions", token, null);
        catalog.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = (await catalog.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        entries.Should().Contain(e => e.GetProperty("permission").GetString() == "identity:accounts:manage"
            && e.GetProperty("sensitivity").GetString() == "Critical");

        var created = await SendAsync(HttpMethod.Post, "/api/identity/roles", token, new { name = "Auditor", displayName = "Auditors" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        (await SendAsync(HttpMethod.Post, "/api/identity/roles", token, new { name = "Auditor" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "the name is taken");
        (await SendAsync(HttpMethod.Post, "/api/identity/roles", token, new { name = "integration:billing" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "the integration prefix is reserved");
        (await SendAsync(HttpMethod.Post, "/api/identity/roles", token, new { name = "   " })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "a blank name is refused");

        var roles = (await (await SendAsync(HttpMethod.Get, "/api/identity/roles", token, null)).Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        roles.Should().Contain("Auditor");

        _audit.Events.Should().Contain(e => e.Action == "identity.role.created" && e.Target == "role:Auditor");
    }

    [Fact]
    public async Task An_unused_role_without_grants_is_deleted_and_the_deletion_is_audited()
    {
        var token = await AdministratorTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/identity/roles", token, new { name = "Retired" })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await SendAsync(HttpMethod.Delete, "/api/identity/roles/Retired", token, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var roles = (await (await SendAsync(HttpMethod.Get, "/api/identity/roles", token, null)).Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        roles.Should().NotContain("Retired");
        _audit.Events.Should().Contain(e => e.Action == "identity.role.deleted" && e.Target == "role:Retired");
    }

    [Fact]
    public async Task A_role_is_not_deleted_while_accounts_hold_it_while_it_holds_grants_or_when_it_is_the_default()
    {
        var token = await AdministratorTokenAsync();
        await EnsureRoleAsync("Holder");
        await EnsureRoleAsync(GrantedRole);
        await CreateUserAsync("holder", "Holder");
        await CreateDefaultRoleAsync("Everyone");

        var inUse = await SendAsync(HttpMethod.Delete, "/api/identity/roles/Holder", token, null);
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await inUse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("ROLE_IN_USE");

        var granted = await SendAsync(HttpMethod.Delete, $"/api/identity/roles/{GrantedRole}", token, null);
        granted.StatusCode.Should().Be(HttpStatusCode.Conflict, "its grants must be cleared first");
        (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("ROLE_HAS_GRANTS");

        (await SendAsync(HttpMethod.Delete, "/api/identity/roles/Everyone", token, null)).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "the default role is never deleted");
        (await SendAsync(HttpMethod.Delete, "/api/identity/roles/integration:billing", token, null)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "the integration prefix is reserved");
        (await SendAsync(HttpMethod.Delete, "/api/identity/roles/NoSuchRole", token, null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        _audit.Events.Should().NotContain(e => e.Action == "identity.role.deleted");
    }

    [Fact]
    public async Task The_built_in_admin_role_is_never_deleted_or_renamed_even_when_no_account_holds_it()
    {
        var token = await AdministratorTokenAsync();
        await EnsureRoleAsync("Admin");

        var deleted = await SendAsync(HttpMethod.Delete, "/api/identity/roles/Admin", token, null);
        deleted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await deleted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("BUILT_IN_ROLE");

        var renamed = await SendAsync(HttpMethod.Put, "/api/identity/roles/Admin", token, new { name = "Owners" });
        renamed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("BUILT_IN_ROLE");

        _audit.Events.Should().NotContain(e => e.Action == "identity.role.deleted" || e.Action == "identity.role.renamed");
    }

    [Fact]
    public async Task A_signed_in_account_without_the_account_permission_cannot_delete_or_rename_a_role()
    {
        await EnsureRoleAsync("Reviewer");
        await CreateUserAsync("plain");
        var (status, body) = await PasswordAsync("plain");
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        (await SendAsync(HttpMethod.Delete, "/api/identity/roles/Reviewer", token, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, "/api/identity/roles/Reviewer", token, new { name = "Other" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_role_read_carries_its_etag_and_a_write_with_a_stale_etag_is_refused()
    {
        var token = await AdministratorTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/identity/roles", token, new { name = "Versioned" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var read = await SendAsync(HttpMethod.Get, "/api/identity/roles/Versioned", token, null);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = read.Headers.ETag!.ToString();
        etag.Should().StartWith("\"").And.EndWith("\"");

        (await SendAsync(HttpMethod.Put, "/api/identity/roles/Versioned", token, new { name = "Renamed" }, "\"stale\"")).StatusCode
            .Should().Be(HttpStatusCode.PreconditionFailed);
        (await SendAsync(HttpMethod.Delete, "/api/identity/roles/Versioned", token, null, "\"stale\"")).StatusCode
            .Should().Be(HttpStatusCode.PreconditionFailed);

        var renamed = await SendAsync(HttpMethod.Put, "/api/identity/roles/Versioned", token, new { name = "Renamed" }, etag);
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        renamed.Headers.ETag.Should().NotBeNull();
        renamed.Headers.ETag!.ToString().Should().NotBe(etag);
    }

    [Fact]
    public async Task A_role_that_does_not_exist_reads_as_not_found()
    {
        var token = await AdministratorTokenAsync();

        (await SendAsync(HttpMethod.Get, "/api/identity/roles/NoSuchRole", token, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unused_role_without_grants_is_renamed_and_the_rename_is_audited()
    {
        var token = await AdministratorTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/identity/roles", token, new { name = "Reviewer" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var renamed = await SendAsync(HttpMethod.Put, "/api/identity/roles/Reviewer", token, new { name = "Auditor" });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("Auditor");

        var roles = (await (await SendAsync(HttpMethod.Get, "/api/identity/roles", token, null)).Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
        roles.Should().Contain("Auditor").And.NotContain("Reviewer");
        _audit.Events.Should().Contain(e => e.Action == "identity.role.renamed" && e.Target == "role:Reviewer");
    }

    [Fact]
    public async Task A_role_is_not_renamed_while_accounts_or_grants_hold_it_or_to_a_reserved_or_taken_name()
    {
        var token = await AdministratorTokenAsync();
        await EnsureRoleAsync("Holder");
        await EnsureRoleAsync(GrantedRole);
        await CreateUserAsync("holder", "Holder");

        (await SendAsync(HttpMethod.Put, "/api/identity/roles/Holder", token, new { name = "Owner" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "accounts hold it");
        (await SendAsync(HttpMethod.Put, $"/api/identity/roles/{GrantedRole}", token, new { name = "Other" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "its grants would be left behind");
        (await SendAsync(HttpMethod.Put, "/api/identity/roles/Holder", token, new { name = "integration:billing" })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, "/api/identity/roles/Holder", token, new { name = "AccountAdmin" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "the name is taken");

        _audit.Events.Should().NotContain(e => e.Action == "identity.role.renamed");
    }

    private async Task<string> AdministratorTokenAsync()
    {
        await EnsureRoleAsync(AccountAdminRole);
        var admin = await CreateUserAsync("admin", AccountAdminRole);
        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task CreateDefaultRoleAsync(string role)
    {
        using var scope = _app.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();
        (await roles.CreateAsync(new ModulusRole { Name = role, IsDefault = true })).Succeeded.Should().BeTrue();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? bearer, object? body, string? ifMatch = null)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
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

    // RFC 6238 (HMAC-SHA1, 30 s, 6 digits) from the base32 key, the algorithm authenticator apps use. Same as the other user admin tests.
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

    /// <summary>Stands in for the grant store: one role still holds grants, every other role is clear.</summary>
    private sealed class GrantedRoleGuard(string grantedRole) : IRoleDeletionGuard
    {
        public Task<string?> BlockingReasonAsync(string role, CancellationToken cancellationToken)
            => Task.FromResult(role == grantedRole ? "The role still holds 1 permission grant(s)." : null);
    }

    private sealed class RecordingAudit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
