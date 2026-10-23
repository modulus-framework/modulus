using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Extensions;
using Modulus.MultiTenancy.Extensions;
using Modulus.MultiTenancy;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Pilot scenario 1 with membership enforced: the real tenant middleware resolves the company from <c>X-Tenant-Id</c> and refuses a
/// caller who is not a member of it. An administrator who belongs to company A lists A's users, and is refused in company B.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UserAdminMembershipTests : OidcHostTestBase
{
    private const string AccountAdminRole = "AccountAdmin";
    private readonly CompanyStore _companies = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMultiTenancy(tenancy => tenancy.UseHeaderResolver().RequireMembership());
        // The middleware resolves a company through the tenant store; the default store knows no company.
        services.Replace(ServiceDescriptor.Singleton<ITenantStore>(_companies));
        services.AddModulusAccountLifecycleAdmin();
        services.AddPermissionGrants(grants => grants.GrantToRole(AccountAdminRole, AccountLifecycleExtensions.ManagePermission));
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.UseMultiTenancy();
        app.MapModulusUserAdmin<ModulusUser>();
    }

    [Fact]
    public async Task A_member_lists_its_company_and_a_non_member_is_refused_in_another_company()
    {
        await EnsureRoleAsync(AccountAdminRole);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        _companies.Add(new TenantInfo(companyA, "company-a"));
        _companies.Add(new TenantInfo(companyB, "company-b"));
        var admin = await CreateHostUserAsync("admin", null);
        await CreateHostUserAsync("ann", companyA);
        _app.Services.GetRequiredService<InMemoryTenantMembershipStore>().Add(admin.Id, companyA);
        await AssignRoleAsync(admin, AccountAdminRole);

        var key = await EnableSecondFactorAsync(admin);
        var (status, body) = await PasswordAsync("admin", mfa: Totp(key));
        status.Should().Be(HttpStatusCode.OK);
        var token = body.GetProperty("access_token").GetString();

        var inCompanyA = await SendAsync("/identity/users", token, companyA);
        inCompanyA.StatusCode.Should().Be(HttpStatusCode.OK);
        var names = (await inCompanyA.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("userName").GetString()).ToList();
        names.Should().Contain("ann");

        var inCompanyB = await SendAsync("/identity/users", token, companyB);
        inCompanyB.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the caller is not a member of that company");
    }

    /// <summary>The companies this host knows, by id.</summary>
    private sealed class CompanyStore : ITenantStore
    {
        private readonly Dictionary<Guid, TenantInfo> _byId = [];

        public void Add(TenantInfo company) => _byId[company.TenantId] = company;

        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_byId.GetValueOrDefault(id));

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
            => Task.FromResult(_byId.Values.FirstOrDefault(c => c.TenantSlug == slug));

        public Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([.. _byId.Values]);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string? bearer, Guid company)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Add("X-Tenant-Id", company.ToString("D"));
        return await client.SendAsync(request);
    }

    /// <summary>Creates an account in the host context. <paramref name="company"/> sets the account's company, or null for a host account.</summary>
    private async Task<ModulusUser> CreateHostUserAsync(string name, Guid? company)
    {
        using var scope = _app.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (tenant.Change(null))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = new ModulusUser { UserName = name, Email = $"{name}@example.test", EmailConfirmed = true, TenantId = company };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            return user;
        }
    }

    private async Task AssignRoleAsync(ModulusUser user, string role)
    {
        using var scope = _app.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using (tenant.Change(null))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var current = (await users.FindByIdAsync(user.Id.ToString()))!;
            (await users.AddToRoleAsync(current, role)).Succeeded.Should().BeTrue();
        }
    }

    private async Task<string> EnableSecondFactorAsync(ModulusUser user)
    {
        using var scope = _app.Services.CreateScope();
        using (scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(null))
        {
            var twoFactor = scope.ServiceProvider.GetRequiredService<IUserTwoFactorService>();
            var setup = (await twoFactor.BeginSetupAsync(user.Id))!;
            var key = setup.SharedKey.Replace(" ", "", StringComparison.Ordinal);
            (await twoFactor.EnableAsync(user.Id, Totp(key))).Should().NotBeNull();
            return key;
        }
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
}
