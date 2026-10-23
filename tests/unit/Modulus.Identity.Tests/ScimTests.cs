using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Modulus.Identity.Scim;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>SCIM 2.0 users and groups over HTTP: company scoping, filters, PATCH dialects, seats and the managed-role list.</summary>
[Trait("Category", "Unit")]
public sealed class ScimTests : IAsyncLifetime
{
    private static readonly Guid s_company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private readonly FakeProvisioner _seats = new();

    private sealed class HeaderTenant(IHttpContextAccessor accessor) : ICurrentTenant
    {
        public Guid? TenantId => accessor.HttpContext?.Request.Headers["X-Test-Tenant"].ToString() is { Length: > 0 } t ? Guid.Parse(t) : null;

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        // Outside a request (seeding) the host sees everything; in a request without a company nothing is visible.
        public bool IsHost => accessor.HttpContext is null;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }

    private sealed class FakeProvisioner : ITenantMemberProvisioner
    {
        public int? Limit { get; set; }

        public HashSet<Guid> Members { get; } = [];

        public Task<bool> AddMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
        {
            if (!Members.Contains(userId) && Limit is { } limit && Members.Count >= limit)
                throw new ConflictException($"The company has reached its limit of {limit} users.");
            Members.Add(userId);
            return Task.FromResult(true);
        }

        public Task<bool> RemoveMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(Members.Remove(userId));
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Authenticated"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
            if (!Request.Headers.ContainsKey("X-Test-NoPermission"))
                claims.Add(new Claim("permission", "scim:manage"));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class FakeEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Acme ERP";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    public Task InitializeAsync() => StartAsync(new Dictionary<string, string?> { ["Identity:Scim:ManagedRoles:0"] = "Buyers" });

    private async Task StartAsync(Dictionary<string, string?> settings)
    {
        if (_app is not null)
            await _app.DisposeAsync();
        _connection?.Dispose();
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeEnvironment());
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        // These hosts carry the caller's permissions as claims; the grant store is the source by default now.
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = true);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentTenant, HeaderTenant>();
        builder.Services.AddSingleton<ITenantMemberProvisioner>(_seats);
        builder.Services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        builder.Services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);
        builder.Services.AddModulusScim<ModulusUser>(configuration);
        // AddIdentity makes the Identity cookie the default scheme; the test principal must win.
        builder.Services.PostConfigure<AuthenticationOptions>(o =>
        {
            o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = o.DefaultForbidScheme = "Test";
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusScim();

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();
            await roles.CreateAsync(new ModulusRole { Name = "Buyers", TenantId = s_company });
            await roles.CreateAsync(new ModulusRole { Name = "Admin", TenantId = s_company });
        }

        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _connection.Dispose();
    }

    private HttpClient Client(Guid? company = null, bool permission = true)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "yes");
        if (company is { } c)
            client.DefaultRequestHeaders.Add("X-Test-Tenant", c.ToString());
        if (!permission)
            client.DefaultRequestHeaders.Add("X-Test-NoPermission", "yes");
        return client;
    }

    private static StringContent Body(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/scim+json");

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static object NewUser(string userName, bool? active = null, string? externalId = null) => new
    {
        schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
        userName,
        externalId,
        name = new { givenName = "Ali", familyName = "Khan" },
        emails = new[] { new { value = userName, primary = true, type = "work" } },
        active,
    };

    private static async Task<string> CreateAsync(HttpClient client, string userName, string? externalId = null)
    {
        var response = await client.PostAsync("/scim/v2/Users", Body(NewUser(userName, externalId: externalId)));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await Json(response)).GetProperty("id").GetString()!;
    }

    private static object Patch(params object[] operations) => new
    {
        schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" },
        Operations = operations,
    };

    private static HttpRequestMessage PatchRequest(string url, object body)
        => new(HttpMethod.Patch, url) { Content = Body(body) };

    [Fact]
    public async Task Creating_a_user_returns_201_with_location_scim_content_type_and_stores_it_in_the_company()
    {
        using var client = Client(s_company);
        var response = await client.PostAsync("/scim/v2/Users", Body(NewUser("ali@acme.test", externalId: "HR-1")));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/scim+json");
        var body = await Json(response);
        var id = body.GetProperty("id").GetString()!;
        response.Headers.Location!.ToString().Should().EndWith($"/scim/v2/Users/{id}");
        body.GetProperty("userName").GetString().Should().Be("ali@acme.test");
        body.GetProperty("externalId").GetString().Should().Be("HR-1");
        body.GetProperty("active").GetBoolean().Should().BeTrue();
        body.GetProperty("name").GetProperty("formatted").GetString().Should().Be("Ali Khan");

        using var other = Client(s_other);
        (await other.GetAsync($"/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_duplicate_user_name_is_409_uniqueness_and_a_duplicate_external_id_too()
    {
        using var client = Client(s_company);
        await CreateAsync(client, "ali@acme.test", "HR-1");

        var duplicate = await client.PostAsync("/scim/v2/Users", Body(NewUser("ali@acme.test")));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(duplicate)).GetProperty("scimType").GetString().Should().Be("uniqueness");

        var sameExternal = await client.PostAsync("/scim/v2/Users", Body(NewUser("sara@acme.test", externalId: "HR-1")));
        sameExternal.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Users_of_another_company_are_invisible_and_a_caller_without_a_company_is_refused()
    {
        using var acme = Client(s_company);
        var id = await CreateAsync(acme, "ali@acme.test");

        using var other = Client(s_other);
        (await other.GetAsync($"/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(await other.GetAsync("/scim/v2/Users"))).GetProperty("totalResults").GetInt32().Should().Be(0);

        using var none = Client();
        var refused = await none.GetAsync("/scim/v2/Users");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(refused)).GetProperty("schemas")[0].GetString().Should().Be("urn:ietf:params:scim:api:messages:2.0:Error");
    }

    [Fact]
    public async Task The_endpoints_need_authentication_and_the_scim_permission()
    {
        using var anonymous = _app.GetTestClient();
        (await anonymous.GetAsync("/scim/v2/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var without = Client(s_company, permission: false);
        (await without.GetAsync("/scim/v2/Users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Filters_pages_and_operators_work_and_unknown_attributes_are_invalidFilter()
    {
        using var client = Client(s_company);
        foreach (var name in new[] { "ali@acme.test", "alina@acme.test", "bob@acme.test", "carol@acme.test" })
            await CreateAsync(client, name);
        var bob = await (await client.GetAsync("/scim/v2/Users?filter=userName eq \"BOB@acme.test\"")).Content.ReadAsStringAsync();
        JsonDocument.Parse(bob).RootElement.GetProperty("totalResults").GetInt32().Should().Be(1);

        async Task<int> Count(string filter)
            => (await Json(await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString(filter)))).GetProperty("totalResults").GetInt32();

        (await Count("userName sw \"ali\"")).Should().Be(2);
        (await Count("userName sw \"ali\" and not (userName ew \"na@acme.test\")")).Should().Be(1);
        (await Count("userName eq \"bob@acme.test\" or userName eq \"carol@acme.test\"")).Should().Be(2);
        (await Count("emails.value co \"acme\" and active eq true")).Should().Be(4);
        (await Count("active eq false")).Should().Be(0);
        (await Count("externalId pr")).Should().Be(0);
        (await Count("userName eq \"x\\\" or true\"")).Should().Be(0);

        var invalid = await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString("passwordHash eq \"x\""));
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(invalid)).GetProperty("scimType").GetString().Should().Be("invalidFilter");
        (await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString("userName eq"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var page = await Json(await client.GetAsync("/scim/v2/Users?startIndex=2&count=2"));
        page.GetProperty("totalResults").GetInt32().Should().Be(4);
        page.GetProperty("startIndex").GetInt32().Should().Be(2);
        page.GetProperty("itemsPerPage").GetInt32().Should().Be(2);
        page.GetProperty("Resources").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Sorting_orders_by_a_supported_attribute_and_refuses_a_bad_sort_order()
    {
        using var client = Client(s_company);
        foreach (var name in new[] { "bob@acme.test", "ali@acme.test", "carol@acme.test" })
            await CreateAsync(client, name);

        async Task<string?> FirstUser(string query)
            => (await Json(await client.GetAsync("/scim/v2/Users?" + query))).GetProperty("Resources")[0].GetProperty("userName").GetString();

        (await FirstUser("sortBy=userName")).Should().Be("ali@acme.test");
        (await FirstUser("sortBy=userName&sortOrder=descending")).Should().Be("carol@acme.test");
        (await FirstUser("sortBy=userName&sortOrder=DESCENDING")).Should().Be("carol@acme.test");
        (await FirstUser("sortBy=notAnAttribute")).Should().NotBeNull("an attribute it cannot sort on is ignored, not refused");

        var bad = await client.GetAsync("/scim/v2/Users?sortBy=userName&sortOrder=sideways");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(bad)).GetProperty("scimType").GetString().Should().Be("invalidValue");

        var groups = await client.GetAsync("/scim/v2/Groups?sortBy=displayName&sortOrder=descending");
        groups.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Json(await client.GetAsync("/scim/v2/ServiceProviderConfig"))).GetProperty("sort").GetProperty("supported").GetBoolean()
            .Should().BeTrue();
    }

    [Fact]
    public async Task A_change_with_a_current_etag_applies_and_a_stale_one_is_refused_with_412_and_writes_nothing()
    {
        using var client = Client(s_company);
        var id = await CreateAsync(client, "etag@acme.test");
        var url = $"/scim/v2/Users/{id}";

        var read = await client.GetAsync(url);
        var first = read.Headers.GetValues("ETag").Single();
        (await Json(read)).GetProperty("meta").GetProperty("version").GetString().Should().Be(first);

        var current = PatchRequest(url, Patch(new { op = "replace", path = "userName", value = "renamed@acme.test" }));
        current.Headers.TryAddWithoutValidation("If-Match", first);
        var applied = await client.SendAsync(current);
        applied.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = applied.Headers.GetValues("ETag").Single();
        second.Should().NotBe(first, "a change moves the tag on");

        var stale = PatchRequest(url, Patch(new { op = "replace", path = "userName", value = "lost@acme.test" }));
        stale.Headers.TryAddWithoutValidation("If-Match", first);
        (await client.SendAsync(stale)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await Json(await client.GetAsync(url))).GetProperty("userName").GetString().Should().Be("renamed@acme.test");

        var delete = new HttpRequestMessage(HttpMethod.Delete, url);
        delete.Headers.TryAddWithoutValidation("If-Match", first);
        (await client.SendAsync(delete)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        var any = new HttpRequestMessage(HttpMethod.Delete, url);
        any.Headers.TryAddWithoutValidation("If-Match", "*");
        (await client.SendAsync(any)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Json(await client.GetAsync("/scim/v2/ServiceProviderConfig"))).GetProperty("etag").GetProperty("supported").GetBoolean()
            .Should().BeTrue();
    }

    [Fact]
    public async Task A_password_is_refused_on_create_replace_and_patch_so_it_is_never_silently_dropped()
    {
        using var client = Client(s_company);

        var create = await client.PostAsync("/scim/v2/Users", Body(new { userName = "pw@acme.test", password = "Sup3r-secret!" }));
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(create)).GetProperty("scimType").GetString().Should().Be("mutability");
        (await Json(await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString("userName eq \"pw@acme.test\""))))
            .GetProperty("totalResults").GetInt32().Should().Be(0, "a refused create writes nothing");

        var id = await CreateAsync(client, "pw-ok@acme.test");
        var replace = await client.PutAsync($"/scim/v2/Users/{id}", Body(new { userName = "pw-ok@acme.test", password = "Sup3r-secret!" }));
        replace.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var patch = await client.SendAsync(PatchRequest($"/scim/v2/Users/{id}", Patch(new { op = "replace", path = "password", value = "Sup3r-secret!" })));
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detail = (await Json(patch)).GetProperty("detail").GetString()!;
        detail.Should().Contain("invitation link");
        detail.Should().NotContain("Sup3r-secret");
    }

    [Fact]
    public async Task Users_carry_meta_created_and_meta_last_modified_and_a_change_moves_last_modified_on()
    {
        using var client = Client(s_company);

        var created = await client.PostAsync("/scim/v2/Users", Body(NewUser("times@acme.test")));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await Json(created);
        var id = body.GetProperty("id").GetString()!;
        var meta = body.GetProperty("meta");
        var createdAt = meta.GetProperty("created").GetDateTimeOffset();
        meta.GetProperty("lastModified").GetDateTimeOffset().Should().Be(createdAt, "nothing has changed since creation");

        await Task.Delay(20);
        var patched = await client.SendAsync(PatchRequest($"/scim/v2/Users/{id}", Patch(
            new { op = "replace", path = "externalId", value = "ext-times" })));
        patched.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await Json(patched)).GetProperty("meta");
        after.GetProperty("created").GetDateTimeOffset().Should().Be(createdAt);
        after.GetProperty("lastModified").GetDateTimeOffset().Should().BeAfter(createdAt);
    }

    [Fact]
    public async Task The_schemas_endpoints_describe_the_user_and_group_resources()
    {
        using var client = Client(s_company);

        (await Json(await client.GetAsync("/scim/v2/Schemas"))).GetProperty("totalResults").GetInt32().Should().Be(2);

        var user = await Json(await client.GetAsync("/scim/v2/Schemas/urn:ietf:params:scim:schemas:core:2.0:User"));
        user.GetProperty("id").GetString().Should().Be("urn:ietf:params:scim:schemas:core:2.0:User");
        var userAttributes = user.GetProperty("attributes").EnumerateArray().ToList();
        userAttributes.Select(a => a.GetProperty("name").GetString()).Should().Contain(new[] { "userName", "emails", "active", "groups" });
        userAttributes.Single(a => a.GetProperty("name").GetString() == "userName").GetProperty("required").GetBoolean().Should().BeTrue();

        var group = await Json(await client.GetAsync("/scim/v2/Schemas/urn:ietf:params:scim:schemas:core:2.0:Group"));
        group.GetProperty("attributes").EnumerateArray().Single(a => a.GetProperty("name").GetString() == "displayName")
            .GetProperty("required").GetBoolean().Should().BeTrue();

        (await client.GetAsync("/scim/v2/Schemas/urn:example:unknown")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_bulk_request_applies_operations_in_order_resolves_bulk_ids_and_stops_at_fail_on_errors()
    {
        using var client = Client(s_company);

        var bulk = new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:BulkRequest" },
            Operations = new object[]
            {
                new { method = "POST", bulkId = "u1", path = "/Users", data = NewUser("bulk@acme.test") },
                new { method = "DELETE", path = "/Users/bulkId:u1" },
            },
        };
        var response = await client.PostAsync("/scim/v2/Bulk", Body(bulk));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ops = (await Json(response)).GetProperty("operations");
        ops.GetArrayLength().Should().Be(2);
        ops[0].GetProperty("status").GetString().Should().Be("201");
        ops[0].GetProperty("bulkId").GetString().Should().Be("u1");
        ops[1].GetProperty("status").GetString().Should().Be("204");
        // DELETE deactivates by default (the account is kept, so it cannot sign in).
        var kept = (await Json(await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString("userName eq \"bulk@acme.test\""))))
            .GetProperty("Resources")[0];
        kept.GetProperty("active").GetBoolean().Should().BeFalse();

        // failOnErrors = 1: the first failure stops the bulk, so the create after it never runs.
        var stopping = new
        {
            failOnErrors = 1,
            Operations = new object[]
            {
                new { method = "POST", path = "/Users", data = new { displayName = "no user name" } },
                new { method = "POST", path = "/Users", data = NewUser("never@acme.test") },
            },
        };
        var stopped = (await Json(await client.PostAsync("/scim/v2/Bulk", Body(stopping)))).GetProperty("operations");
        stopped.GetArrayLength().Should().Be(1);
        stopped[0].GetProperty("status").GetString().Should().Be("400");
        (await Json(await client.GetAsync("/scim/v2/Users?filter=" + Uri.EscapeDataString("userName eq \"never@acme.test\""))))
            .GetProperty("totalResults").GetInt32().Should().Be(0);

        (await Json(await client.GetAsync("/scim/v2/ServiceProviderConfig"))).GetProperty("bulk").GetProperty("supported").GetBoolean()
            .Should().BeTrue();
    }

    [Fact]
    public async Task Patch_in_the_azure_dialect_deactivates_renames_and_changes_the_email()
    {
        using var client = Client(s_company);
        var id = await CreateAsync(client, "ali@acme.test");

        var response = await client.SendAsync(PatchRequest($"/scim/v2/Users/{id}", Patch(
            new { op = "Replace", path = "active", value = "False" },
            new { op = "Replace", path = "name.givenName", value = "Aliya" },
            new { op = "Replace", path = "emails[type eq \"work\"].value", value = "aliya@acme.test" },
            new { op = "Add", path = "displayName", value = "ignored" })));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body.GetProperty("active").GetBoolean().Should().BeFalse();
        body.GetProperty("name").GetProperty("givenName").GetString().Should().Be("Aliya");
        body.GetProperty("emails")[0].GetProperty("value").GetString().Should().Be("aliya@acme.test");
        _seats.Members.Should().BeEmpty("a deactivated account frees its seat");
    }

    [Fact]
    public async Task Patch_without_a_path_takes_an_object_of_attributes_and_refuses_read_only_ones()
    {
        using var client = Client(s_company);
        var id = await CreateAsync(client, "ali@acme.test");

        var ok = await client.SendAsync(PatchRequest($"/scim/v2/Users/{id}", Patch(
            new { op = "replace", value = new { active = false, externalId = "HR-9" } })));
        var body = await Json(ok);
        body.GetProperty("active").GetBoolean().Should().BeFalse();
        body.GetProperty("externalId").GetString().Should().Be("HR-9");

        var readOnly = await client.SendAsync(PatchRequest($"/scim/v2/Users/{id}", Patch(new { op = "replace", path = "id", value = "x" })));
        readOnly.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(readOnly)).GetProperty("scimType").GetString().Should().Be("mutability");

        (await client.SendAsync(PatchRequest($"/scim/v2/Users/{id}", Patch(new { op = "explode", path = "active", value = true }))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.SendAsync(PatchRequest($"/scim/v2/Users/{id}", new { Operations = Array.Empty<object>() })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_seat_limit_refuses_new_and_reactivated_accounts_and_a_refused_create_leaves_nothing_behind()
    {
        _seats.Limit = 1;
        using var client = Client(s_company);
        var first = await CreateAsync(client, "ali@acme.test");

        var refused = await client.PostAsync("/scim/v2/Users", Body(NewUser("sara@acme.test")));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(await client.GetAsync("/scim/v2/Users"))).GetProperty("totalResults").GetInt32().Should().Be(1);

        // Deactivating frees the seat; the next person can take it; re-activating the first is then refused.
        await client.SendAsync(PatchRequest($"/scim/v2/Users/{first}", Patch(new { op = "replace", path = "active", value = false })));
        var second = await CreateAsync(client, "sara@acme.test");
        var back = await client.SendAsync(PatchRequest($"/scim/v2/Users/{first}", Patch(new { op = "replace", path = "active", value = true })));
        back.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(await client.GetAsync($"/scim/v2/Users/{first}"))).GetProperty("active").GetBoolean().Should().BeFalse();
        _seats.Members.Should().ContainSingle().Which.Should().Be(Guid.Parse(second));
    }

    [Fact]
    public async Task Put_replaces_the_profile_and_delete_deactivates_by_default()
    {
        using var client = Client(s_company);
        var id = await CreateAsync(client, "ali@acme.test");

        var put = await client.PutAsync($"/scim/v2/Users/{id}", Body(new
        {
            userName = "ali@acme.test",
            name = new { givenName = "Ali", familyName = "Raza" },
            emails = new[] { new { value = "ali@acme.test", primary = true } },
            active = true,
        }));
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(put)).GetProperty("name").GetProperty("familyName").GetString().Should().Be("Raza");

        (await client.DeleteAsync($"/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Json(await client.GetAsync($"/scim/v2/Users/{id}"))).GetProperty("active").GetBoolean().Should().BeFalse();
        (await client.GetAsync($"/scim/v2/Users/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/scim/v2/Users/not-a-guid")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_removes_the_account_when_configured()
    {
        await StartAsync(new Dictionary<string, string?> { ["Identity:Scim:DeleteRemovesAccount"] = "true" });
        using var client = Client(s_company);
        var id = await CreateAsync(client, "ali@acme.test");

        (await client.DeleteAsync($"/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/scim/v2/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_membership_change_moves_the_group_etag_so_a_stale_group_change_is_refused()
    {
        using var client = Client(s_company);
        var userId = await CreateAsync(client, "member@acme.test");
        var groupId = (await Json(await client.GetAsync("/scim/v2/Groups"))).GetProperty("Resources")[0].GetProperty("id").GetString()!;
        var url = $"/scim/v2/Groups/{groupId}";

        var before = (await client.GetAsync(url)).Headers.GetValues("ETag").Single();

        var add = await client.SendAsync(PatchRequest(url, Patch(
            new { op = "Add", path = "members", value = new[] { new { value = userId } } })));
        add.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = add.Headers.GetValues("ETag").Single();
        after.Should().NotBe(before, "the membership is part of the group's representation");

        var stale = PatchRequest(url, Patch(new { op = "Remove", path = "members", value = new[] { new { value = userId } } }));
        stale.Headers.TryAddWithoutValidation("If-Match", before);
        (await client.SendAsync(stale)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await Json(await client.GetAsync(url))).GetProperty("members").GetArrayLength().Should().Be(1, "the refused change wrote nothing");
    }

    [Fact]
    public async Task Groups_expose_only_managed_roles_and_membership_changes_the_users_roles()
    {
        using var client = Client(s_company);
        var userId = await CreateAsync(client, "ali@acme.test");

        var list = await Json(await client.GetAsync("/scim/v2/Groups"));
        list.GetProperty("totalResults").GetInt32().Should().Be(1);
        var group = list.GetProperty("Resources")[0];
        group.GetProperty("displayName").GetString().Should().Be("Buyers");
        var groupId = group.GetProperty("id").GetString()!;

        // Adding a member (Azure sends capitalised ops).
        var add = await client.SendAsync(PatchRequest($"/scim/v2/Groups/{groupId}", Patch(
            new { op = "Add", path = "members", value = new[] { new { value = userId } } })));
        add.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(add)).GetProperty("members").GetArrayLength().Should().Be(1);
        var user = await Json(await client.GetAsync($"/scim/v2/Users/{userId}"));
        user.GetProperty("groups")[0].GetProperty("display").GetString().Should().Be("Buyers");

        var groupAgain = await Json(await client.GetAsync($"/scim/v2/Groups/{groupId}"));
        groupAgain.GetProperty("members")[0].GetProperty("value").GetString().Should().Be(userId);

        var remove = await client.SendAsync(PatchRequest($"/scim/v2/Groups/{groupId}", Patch(
            new { op = "remove", path = $"members[value eq \"{userId}\"]" })));
        (await Json(remove)).GetProperty("members").GetArrayLength().Should().Be(0);

        // A member must be an account of this company.
        var foreign = await client.SendAsync(PatchRequest($"/scim/v2/Groups/{groupId}", Patch(
            new { op = "add", path = "members", value = new[] { new { value = Guid.NewGuid() } } })));
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_role_that_is_not_managed_is_not_found_and_cannot_be_renamed_or_created_by_default()
    {
        using var client = Client(s_company);
        // The Admin role exists but is not on the managed list.
        var all = await Json(await client.GetAsync("/scim/v2/Groups"));
        all.GetProperty("Resources").EnumerateArray().Select(g => g.GetProperty("displayName").GetString()).Should().NotContain("Admin");
        (await client.GetAsync($"/scim/v2/Groups/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var buyers = (await Json(await client.GetAsync("/scim/v2/Groups"))).GetProperty("Resources")[0].GetProperty("id").GetString();
        var rename = await client.SendAsync(PatchRequest($"/scim/v2/Groups/{buyers}", Patch(new { op = "replace", path = "displayName", value = "Sellers" })));
        rename.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(rename)).GetProperty("scimType").GetString().Should().Be("mutability");

        (await client.PostAsync("/scim/v2/Groups", Body(new { displayName = "Auditors" }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync($"/scim/v2/Groups/{buyers}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Role_creation_is_allowed_only_for_managed_names_when_enabled()
    {
        await StartAsync(new Dictionary<string, string?>
        {
            ["Identity:Scim:ManagedRoles:0"] = "Buyers",
            ["Identity:Scim:ManagedRoles:1"] = "Planners",
            ["Identity:Scim:AllowRoleCreation"] = "true",
        });
        using var client = Client(s_company);

        (await client.PostAsync("/scim/v2/Groups", Body(new { displayName = "Admin" }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync("/scim/v2/Groups", Body(new { displayName = "Planners" }))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsync("/scim/v2/Groups", Body(new { displayName = "Planners" }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Discovery_documents_describe_the_server()
    {
        using var client = Client(s_company);
        var config = await Json(await client.GetAsync("/scim/v2/ServiceProviderConfig"));
        config.GetProperty("patch").GetProperty("supported").GetBoolean().Should().BeTrue();
        config.GetProperty("bulk").GetProperty("supported").GetBoolean().Should().BeTrue("bulk is served (RFC 7644 §3.7)");
        (await Json(await client.GetAsync("/scim/v2/ResourceTypes"))).GetProperty("totalResults").GetInt32().Should().Be(2);
    }
}
