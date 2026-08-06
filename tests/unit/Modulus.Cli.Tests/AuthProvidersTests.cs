using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class AuthProvidersTests
{
    [Fact]
    public void All_contains_eight_choices()
    {
        // none + openiddict + 6 external providers = 8
        AuthProviders.All.Should().HaveCount(8);
    }

    [Fact]
    public void All_includes_none_as_first_choice()
    {
        AuthProviders.All[0].Key.Should().Be("none");
        AuthProviders.All[0].AddMethod.Should().BeNull();
    }

    [Fact]
    public void All_includes_openiddict_with_correct_metadata()
    {
        var openiddict = AuthProviders.Find("openiddict");
        openiddict.Should().NotBeNull();
        openiddict!.AddMethod.Should().Be("AddModulusOpenIddict");
        openiddict.Namespace.Should().Be("Modulus.Identity.Extensions");
        openiddict.ConfigKey.Should().Be("Identity");
    }

    [Theory]
    [InlineData("auth0", "AddAuth0", "Modulus.Identity.Auth0", "Identity:ExternalProviders:Auth0")]
    [InlineData("authentik", "AddAuthentik", "Modulus.Identity.Authentik", "Identity:ExternalProviders:Authentik")]
    [InlineData("azuread", "AddAzureAd", "Modulus.Identity.AzureAd", "Identity:ExternalProviders:AzureAd")]
    [InlineData("duende", "AddDuendeIdentityServer", "Modulus.Identity.Duende", "Identity:ExternalProviders:Duende")]
    [InlineData("keycloak", "AddKeycloak", "Modulus.Identity.Keycloak", "Identity:ExternalProviders:Keycloak")]
    [InlineData("okta", "AddOkta", "Modulus.Identity.Okta", "Identity:ExternalProviders:Okta")]
    public void External_providers_have_correct_metadata(
        string key, string addMethod, string ns, string configKey)
    {
        var provider = AuthProviders.Find(key);
        provider.Should().NotBeNull();
        provider!.Key.Should().Be(key);
        provider.AddMethod.Should().Be(addMethod);
        provider.Namespace.Should().Be(ns);
        provider.ConfigKey.Should().Be(configKey);
        provider.ConfigJson.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("auth0")]
    [InlineData("authentik")]
    [InlineData("azuread")]
    [InlineData("duende")]
    [InlineData("keycloak")]
    [InlineData("okta")]
    public void IsExternalProvider_true_for_six_providers(string key)
    {
        AuthProviders.IsExternalProvider(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("openiddict")]
    [InlineData("xyz")]
    [InlineData("")]
    public void IsExternalProvider_false_for_non_external(string key)
    {
        AuthProviders.IsExternalProvider(key).Should().BeFalse();
    }

    [Fact]
    public void Find_returns_null_for_unknown_key()
    {
        AuthProviders.Find("fingerprint").Should().BeNull();
    }

    [Fact]
    public void Find_returns_null_for_null_or_empty()
    {
        AuthProviders.Find(null).Should().BeNull();
        AuthProviders.Find("").Should().BeNull();
        AuthProviders.Find("   ").Should().BeNull();
    }

    [Theory]
    [InlineData("NONE")]
    [InlineData("OpenIddict")]
    [InlineData("KEYCLOAK")]
    public void Find_is_case_insensitive(string key)
    {
        AuthProviders.Find(key).Should().NotBeNull();
    }

    [Fact]
    public void Keys_contains_all_eight_keys()
    {
        AuthProviders.Keys.Should().BeEquivalentTo(
            new[] { "none", "openiddict", "auth0", "authentik", "azuread", "duende", "keycloak", "okta" });
    }

    [Fact]
    public void DisplayChoices_has_same_count_as_all()
    {
        AuthProviders.DisplayChoices.Should().HaveCount(AuthProviders.All.Length);
    }

    [Fact]
    public void All_providers_have_unique_keys()
    {
        AuthProviders.All.Select(p => p.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_providers_have_unique_display_names()
    {
        AuthProviders.All.Select(p => p.DisplayName).Should().OnlyHaveUniqueItems();
    }
}
