namespace Modulus.Identity.Tests;

using System.Security.Claims;
using FluentAssertions;
using Modulus.Identity.Abstractions;
using Xunit;

/// <summary>
/// Each supported identity provider names its claims differently. These tests pin the claim each profile reads, so a
/// provider's token maps to the same identity whichever server issued it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FederatedProviderProfileTests
{
    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["app-admins"] = "Admin",
    };

    private static FederatedLoginOptions Options(string provider) => new()
    {
        Provider = provider,
        RoleMap = new(Groups, StringComparer.OrdinalIgnoreCase),
    };

    private static ClaimsPrincipal Token(params Claim[] claims) => new(new ClaimsIdentity(claims, "idp"));

    [Fact]
    public void Every_profile_is_known_and_names_its_token_to_send()
    {
        foreach (var name in FederatedProviders.Names)
        {
            FederatedProviders.IsKnown(name).Should().BeTrue();
            FederatedProviders.Get(name).TokenToSend.Should().NotBeNullOrWhiteSpace();
        }

        FederatedProviders.IsKnown("keycloak").Should().BeTrue("provider names match case-insensitively");
        FederatedProviders.IsKnown("Nope").Should().BeFalse();
    }

    [Fact]
    public void An_unknown_provider_is_refused_at_startup_with_the_known_names()
    {
        var options = new FederatedLoginOptions { Provider = "Nope" };

        options.IsValid(out var problem).Should().BeFalse();
        problem.Should().Contain("Nope").And.Contain("Keycloak");
    }

    [Fact]
    public void Keycloak_reads_realm_roles_nested_under_realm_access()
    {
        var token = Token(
            new Claim("sub", "kc-1"),
            new Claim("realm_access", "{\"roles\":[\"app-admins\",\"offline_access\"]}"));

        var identity = FederatedClaims.Read(token, Options(FederatedProviders.Keycloak));

        identity!.LocalRoles.Should().Equal("Admin");
    }

    [Fact]
    public void Entra_ID_uses_the_stable_object_id_and_app_roles()
    {
        var token = Token(
            new Claim("oid", "00000000-aaaa"),
            new Claim("sub", "pairwise-per-app"),
            new Claim("roles", "app-admins"),
            new Claim("preferred_username", "alice@contoso.com"),
            new Claim("name", "Alice Smith"));

        var identity = FederatedClaims.Read(token, Options(FederatedProviders.AzureAd));

        identity!.Subject.Should().Be("00000000-aaaa", "sub is pairwise per application and differs between apps");
        identity.LocalRoles.Should().Equal("Admin");
        identity.EmailVerified.Should().BeFalse("Entra ID has no email_verified claim unless the tenant emits xms_edov");
    }

    [Fact]
    public void Auth0_reads_roles_from_the_namespaced_claim_once_it_is_named()
    {
        const string claim = "https://example.com/roles";
        var token = Token(
            new Claim("sub", "auth0|123"),
            new Claim(claim, "app-admins"));

        var unnamed = FederatedClaims.Read(token, Options(FederatedProviders.Auth0));
        var named = FederatedClaims.Read(token, new FederatedLoginOptions
        {
            Provider = FederatedProviders.Auth0,
            RolesClaim = claim,
            RoleMap = new(Groups, StringComparer.OrdinalIgnoreCase),
        });

        unnamed!.LocalRoles.Should().BeEmpty("the default 'roles' claim does not match a namespaced one");
        named!.LocalRoles.Should().Equal("Admin");
        named.Subject.Should().Be("auth0|123", "subjects with a separator are kept as written");
    }

    [Fact]
    public void OpenIddict_and_Duende_read_the_role_claim()
    {
        var token = Token(new Claim("sub", "u-1"), new Claim("role", "app-admins"));

        FederatedClaims.Read(token, Options(FederatedProviders.OpenIddict))!.LocalRoles.Should().Equal("Admin");
        FederatedClaims.Read(token, Options(FederatedProviders.Duende))!.LocalRoles.Should().Equal("Admin");
    }

    [Fact]
    public void Okta_and_Authentik_read_groups_and_verified_email()
    {
        var token = Token(
            new Claim("sub", "okta-1"),
            new Claim("email", "bob@contoso.com"),
            new Claim("email_verified", "true"),
            new Claim("groups", "app-admins"));

        var okta = FederatedClaims.Read(token, Options(FederatedProviders.Okta));
        var authentik = FederatedClaims.Read(token, Options(FederatedProviders.Authentik));

        okta!.LocalRoles.Should().Equal("Admin");
        okta.EmailVerified.Should().BeTrue();
        authentik!.LocalRoles.Should().Equal("Admin");
    }

    [Fact]
    public void An_explicit_setting_overrides_the_profile()
    {
        var token = Token(new Claim("sub", "x"), new Claim("uid", "custom-subject"));
        var options = Options(FederatedProviders.AzureAd);
        options.SubjectClaim = "uid";

        FederatedClaims.Read(token, options)!.Subject.Should().Be("custom-subject");
    }

    [Fact]
    public void A_role_value_inside_a_json_array_claim_is_read_as_a_list()
    {
        var token = Token(new Claim("sub", "kc-2"), new Claim("realm_access", "{\"roles\":[\"app-admins\"]}"));

        FederatedClaims.RoleValues(token, "realm_access.roles").Should().Equal("app-admins");
        FederatedClaims.RoleValues(token, "realm_access.missing").Should().BeEmpty();
        FederatedClaims.RoleValues(token, "not-a-claim.roles").Should().BeEmpty();
    }

    [Fact]
    public void A_malformed_json_claim_yields_no_roles_instead_of_throwing()
    {
        var token = Token(new Claim("sub", "x"), new Claim("realm_access", "{not json"));

        FederatedClaims.RoleValues(token, "realm_access.roles").Should().BeEmpty();
    }
}
