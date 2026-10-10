namespace Modulus.Identity.Tests;

using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;
using AppUser = TokenPrincipalFactoryTests.AppUser;

[Trait("Category", "Unit")]
public sealed class FederatedLoginOptionsTests
{
    private static FederatedLoginOptions Enabled(string metadata = "https://idp.test/.well-known/openid-configuration") => new()
    {
        Enabled = true,
        MetadataAddress = metadata,
        Audiences = ["modulus-app"],
    };

    [Fact]
    public void Disabled_settings_are_never_checked()
        => new FederatedLoginOptions().IsValid(out _).Should().BeTrue();

    [Fact]
    public void A_valid_enabled_configuration_passes()
        => Enabled().IsValid(out _).Should().BeTrue();

    [Fact]
    public void Enabled_without_a_discovery_address_is_refused()
    {
        var options = Enabled(metadata: "");
        options.IsValid(out var problem).Should().BeFalse();
        problem.Should().Contain("MetadataAddress");
    }

    [Fact]
    public void Plain_http_discovery_is_refused_unless_it_is_loopback()
    {
        Enabled("http://idp.test/.well-known/openid-configuration").IsValid(out _).Should().BeFalse();
        Enabled("http://localhost:8080/.well-known/openid-configuration").IsValid(out _).Should().BeTrue();
    }

    [Fact]
    public void Enabled_without_an_audience_is_refused()
    {
        var options = Enabled();
        options.Audiences = [" "];
        options.IsValid(out var problem).Should().BeFalse();
        problem.Should().Contain("Audiences");
    }
}

[Trait("Category", "Unit")]
public sealed class FederatedClaimsTests
{
    private static readonly FederatedLoginOptions Options = new()
    {
        RoleMap = new(StringComparer.OrdinalIgnoreCase) { ["app-admins"] = "Admin", ["app-editors"] = "Editor" },
    };

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void A_token_without_a_subject_yields_no_identity()
        => FederatedClaims.Read(Principal(new Claim("email", "a@b.test")), Options).Should().BeNull();

    [Fact]
    public void Only_mapped_groups_become_roles_and_matching_ignores_case()
    {
        var identity = FederatedClaims.Read(Principal(
            new Claim("sub", "s-1"),
            new Claim("groups", "APP-ADMINS"),
            new Claim("groups", "everyone"),
            new Claim("groups", "app-editors")), Options);

        identity.Should().NotBeNull();
        identity!.Subject.Should().Be("s-1");
        identity.LocalRoles.Should().Equal("Admin", "Editor");
    }

    [Fact]
    public void Email_is_verified_only_when_the_provider_says_so()
    {
        var verified = FederatedClaims.Read(Principal(new Claim("sub", "s"), new Claim("email", "a@b.test"), new Claim("email_verified", "true")), Options);
        var unverified = FederatedClaims.Read(Principal(new Claim("sub", "s"), new Claim("email", "a@b.test"), new Claim("email_verified", "false")), Options);

        verified!.EmailVerified.Should().BeTrue();
        unverified!.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public void An_empty_domain_list_admits_every_address_and_a_set_list_needs_a_match()
    {
        FederatedClaims.EmailDomainAllowed(null, []).Should().BeTrue();
        FederatedClaims.EmailDomainAllowed("a@Contoso.test", ["contoso.test"]).Should().BeTrue();
        FederatedClaims.EmailDomainAllowed("a@other.test", ["contoso.test"]).Should().BeFalse();
        FederatedClaims.EmailDomainAllowed(null, ["contoso.test"]).Should().BeFalse();
        FederatedClaims.EmailDomainAllowed("a@", ["contoso.test"]).Should().BeFalse();
    }
}

[Trait("Category", "Unit")]
public sealed class FederatedTokenReadingTests
{
    private const string Issuer = "https://idp.test";
    private const string Audience = "modulus-app";

    private static (SigningCredentials Creds, SecurityKey Key) MakeKey()
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "k1" };
        return (new SigningCredentials(key, SecurityAlgorithms.RsaSha256), key);
    }

    private static string MakeToken(SigningCredentials creds, string audience = Audience, DateTime? expires = null)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            SigningCredentials = creds,
            Expires = expires,
            Claims = new Dictionary<string, object> { ["sub"] = "idp-user-1", ["email"] = "a@b.test", ["groups"] = new[] { "app-admins", "staff" } },
        });

    private static TokenValidationParameters Parameters(SecurityKey key) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = key,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
    };

    [Fact]
    public async Task A_valid_provider_token_returns_its_claims()
    {
        var (creds, key) = MakeKey();

        var principal = await ExternalTokenValidator.ReadJwtAsync(MakeToken(creds), Parameters(key));

        principal.Should().NotBeNull();
        principal!.FindFirst("sub")!.Value.Should().Be("idp-user-1");
        principal.FindAll("groups").Select(c => c.Value).Should().Equal("app-admins", "staff");
    }

    [Fact]
    public async Task A_token_for_another_audience_is_refused()
    {
        var (creds, key) = MakeKey();

        (await ExternalTokenValidator.ReadJwtAsync(MakeToken(creds, audience: "someone-else"), Parameters(key))).Should().BeNull();
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var (creds, key) = MakeKey();

        (await ExternalTokenValidator.ReadJwtAsync(MakeToken(creds, expires: DateTime.UtcNow.AddHours(-2)), Parameters(key))).Should().BeNull();
    }

    [Fact]
    public async Task A_blank_token_is_refused_without_throwing()
    {
        var (_, key) = MakeKey();

        (await ExternalTokenValidator.ReadJwtAsync("   ", Parameters(key))).Should().BeNull();
    }
}

/// <summary>
/// The exchange against a real Identity store (SQLite in memory): account creation and linking, role sync, and every refusal.
/// The provider's token is replaced by a fake reader so the tests need no discovery endpoint.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FederatedLoginValidatorTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly FakeReader _reader = new();
    private readonly FederatedLoginOptions _options = new()
    {
        Enabled = true,
        MetadataAddress = "https://idp.test/.well-known/openid-configuration",
        Audiences = ["modulus-app"],
        RoleMap = new(StringComparer.OrdinalIgnoreCase) { ["app-admins"] = "Admin", ["app-editors"] = "Editor" },
    };

    public FederatedLoginValidatorTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(new HostTenant());
        services.AddDbContext<ModulusIdentityDbContext<AppUser, ModulusRole>>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<AppUser>(o => o.SignIn.RequireConfirmedEmail = true)
            .AddRoles<ModulusRole>()
            .AddEntityFrameworkStores<ModulusIdentityDbContext<AppUser, ModulusRole>>();

        _services = services.BuildServiceProvider();
        _scope = _services.CreateAsyncScope();
        _scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext<AppUser, ModulusRole>>()
            .Database.EnsureCreated();
    }

    private UserManager<AppUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

    private RoleManager<ModulusRole> Roles => _scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();

    private FederatedLoginValidator<AppUser, ModulusRole> Validator() => new(_reader, Options.Create(_options), Users, Roles);

    [Fact]
    public async Task A_first_login_creates_a_passwordless_linked_account_with_the_mapped_role()
    {
        await EnsureRoleAsync("Admin");
        _reader.Add("token", Token("sub-alice", "alice@contoso.test", verified: true, name: "Alice Smith", groups: "app-admins"));

        var result = await Validator().ValidateAsync("token");

        result.Success.Should().BeTrue();
        var user = await Users.FindByLoginAsync("federated", "sub-alice");
        user.Should().NotBeNull();
        result.Subject.Should().Be(user!.Id.ToString());
        result.Roles.Should().Equal("Admin");
        result.SecurityStamp.Should().NotBeNullOrEmpty();
        user.Email.Should().Be("alice@contoso.test");
        user.EmailConfirmed.Should().BeTrue();
        user.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
        (await Users.HasPasswordAsync(user)).Should().BeFalse("federated accounts sign in only through the provider");
    }

    [Fact]
    public async Task Later_logins_follow_the_provider_groups_and_keep_roles_granted_locally()
    {
        await EnsureRoleAsync("Admin");
        await EnsureRoleAsync("Editor");
        await EnsureRoleAsync("Support");
        _reader.Add("first", Token("sub-bob", "bob@contoso.test", verified: true, groups: "app-admins,app-editors".Split(',')));
        await Validator().ValidateAsync("first");
        var bob = await Users.FindByLoginAsync("federated", "sub-bob");
        (await Users.AddToRoleAsync(bob!, "Support")).Succeeded.Should().BeTrue();

        _reader.Add("second", Token("sub-bob", "bob@contoso.test", verified: true, groups: "app-editors"));
        var result = await Validator().ValidateAsync("second");

        result.Success.Should().BeTrue();
        result.Roles.Should().BeEquivalentTo(["Editor", "Support"], "Admin left the group; Support was granted locally");
        (await Users.FindByNameAsync("bob@contoso.test"))!.Id.Should().Be(bob!.Id, "the same account is reused");
    }

    [Fact]
    public async Task Without_create_unknown_users_a_stranger_is_refused_and_no_account_is_made()
    {
        _options.CreateUnknownUsers = false;
        _reader.Add("token", Token("sub-carol", "carol@contoso.test", verified: true));

        (await Validator().ValidateAsync("token")).Success.Should().BeFalse();
        (await Users.FindByNameAsync("carol@contoso.test")).Should().BeNull();
    }

    [Fact]
    public async Task An_unverified_email_never_claims_an_existing_account()
    {
        await CreateLocalUserAsync("dave", "dave@contoso.test");
        _options.LinkByVerifiedEmail = true;
        _reader.Add("token", Token("sub-mallory", "dave@contoso.test", verified: false));

        (await Validator().ValidateAsync("token")).Success.Should().BeFalse();
        (await Users.FindByLoginAsync("federated", "sub-mallory")).Should().BeNull();
    }

    [Fact]
    public async Task A_verified_email_links_to_the_existing_account_only_when_the_option_is_on()
    {
        var existing = await CreateLocalUserAsync("erin", "erin@contoso.test");
        _reader.Add("token", Token("sub-erin", "erin@contoso.test", verified: true));

        (await Validator().ValidateAsync("token")).Success.Should().BeFalse("linking is off by default");

        _options.LinkByVerifiedEmail = true;
        var result = await Validator().ValidateAsync("token");

        result.Success.Should().BeTrue();
        result.Subject.Should().Be(existing.Id.ToString());
        (await Users.FindByLoginAsync("federated", "sub-erin"))!.Id.Should().Be(existing.Id);
    }

    [Fact]
    public async Task An_unknown_or_forged_token_is_refused()
    {
        (await Validator().ValidateAsync("never-issued")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_without_a_subject_is_refused()
    {
        _reader.Add("token", Token(" ", "frank@contoso.test", verified: true));

        (await Validator().ValidateAsync("token")).Success.Should().BeFalse();
        (await Users.FindByNameAsync("frank@contoso.test")).Should().BeNull();
    }

    [Fact]
    public async Task A_disabled_linked_account_is_refused()
    {
        var user = await CreateLocalUserAsync("gina", "gina@contoso.test", isActive: false);
        (await Users.AddLoginAsync(user, new UserLoginInfo("federated", "sub-gina", "federated"))).Succeeded.Should().BeTrue();
        _reader.Add("token", Token("sub-gina", "gina@contoso.test", verified: true));

        (await Validator().ValidateAsync("token")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task An_address_outside_the_allowed_domains_is_refused_before_any_account_is_made()
    {
        _options.AllowedEmailDomains = ["contoso.test"];
        _reader.Add("token", Token("sub-hal", "hal@other.test", verified: true));

        (await Validator().ValidateAsync("token")).Success.Should().BeFalse();
        (await Users.FindByNameAsync("hal@other.test")).Should().BeNull();
    }

    [Fact]
    public async Task A_new_account_whose_email_the_provider_has_not_verified_cannot_sign_in_yet()
    {
        _reader.Add("token", Token("sub-ivy", "ivy@contoso.test", verified: false));

        (await Validator().ValidateAsync("token")).Success.Should().BeFalse("RequireConfirmedEmail holds the account back");
        (await Users.FindByNameAsync("ivy@contoso.test")).Should().NotBeNull("the account exists, unconfirmed");
    }

    private static Claim[] Claims(string subject, string? email, bool verified, string? name, string[] groups)
    {
        var claims = new List<Claim> { new("sub", subject) };
        if (email is not null)
        {
            claims.Add(new Claim("email", email));
            claims.Add(new Claim("email_verified", verified ? "true" : "false"));
        }

        if (name is not null)
            claims.Add(new Claim("name", name));

        claims.AddRange(groups.Select(g => new Claim("groups", g)));
        return [.. claims];
    }

    private static ClaimsPrincipal Token(string subject, string? email = null, bool verified = false, string? name = null, params string[] groups)
        => new(new ClaimsIdentity(Claims(subject, email, verified, name, groups), "idp"));

    private async Task<AppUser> CreateLocalUserAsync(string userName, string email, bool isActive = true)
    {
        var user = new AppUser { UserName = userName, Email = email, EmailConfirmed = true, IsActive = isActive };
        (await Users.CreateAsync(user)).Succeeded.Should().BeTrue();
        return user;
    }

    private async Task EnsureRoleAsync(string role)
    {
        if (!await Roles.RoleExistsAsync(role))
            (await Roles.CreateAsync(new ModulusRole { Id = Guid.NewGuid(), Name = role })).Succeeded.Should().BeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class FakeReader : IFederatedTokenReader
    {
        private readonly Dictionary<string, ClaimsPrincipal> _tokens = new();

        public void Add(string token, ClaimsPrincipal principal) => _tokens[token] = principal;

        public Task<ClaimsPrincipal?> ReadAsync(string token, CancellationToken ct = default)
            => Task.FromResult(_tokens.GetValueOrDefault(token));
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;

        public string? TenantSlug => null;

        public bool IsAvailable => false;

        public bool IsHost => true;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
