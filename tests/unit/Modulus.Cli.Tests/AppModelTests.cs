using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class AppModelTests
{
    [Fact]
    public void Default_auth_is_none()
    {
        var model = new AppModel();
        model.Auth.Should().Be("none");
        model.UseAuth.Should().BeFalse();
        model.UseOpenIddict.Should().BeFalse();
        model.UseExternalProvider.Should().BeFalse();
    }

    [Fact]
    public void OpenIddict_sets_correct_flags()
    {
        var model = new AppModel { Auth = "openiddict" };
        model.UseAuth.Should().BeTrue();
        model.UseOpenIddict.Should().BeTrue();
        model.UseExternalProvider.Should().BeFalse();
        model.IdentityConfigJson.Should().Contain("UseDevelopmentCertificates");
    }

    [Theory]
    [InlineData("auth0")]
    [InlineData("authentik")]
    [InlineData("azuread")]
    [InlineData("duende")]
    [InlineData("keycloak")]
    [InlineData("okta")]
    public void External_provider_sets_correct_flags(string auth)
    {
        var model = new AppModel { Auth = auth };
        model.UseAuth.Should().BeTrue();
        model.UseOpenIddict.Should().BeFalse();
        model.UseExternalProvider.Should().BeTrue();
        model.ExternalProviderName.Should().NotBeNullOrEmpty();
        model.ExternalProviderAddMethod.Should().NotBeNullOrEmpty();
        model.ExternalProviderNamespace.Should().NotBeNullOrEmpty();
        model.ExternalProviderConfigKey.Should().NotBeNullOrEmpty();
        model.IdentityConfigJson.Should().Contain("ExternalProviders");
    }

    [Fact]
    public void Keycloak_has_correct_derived_properties()
    {
        var model = new AppModel { Auth = "keycloak" };
        model.ExternalProviderName.Should().Be("Keycloak");
        model.ExternalProviderAddMethod.Should().Be("AddKeycloak");
        model.ExternalProviderNamespace.Should().Be("Modulus.Identity.Keycloak");
        model.ExternalProviderConfigKey.Should().Be("Identity:ExternalProviders:Keycloak");
        model.IdentityConfigJson.Should().Contain("Realm");
    }

    [Fact]
    public void AzureAd_has_correct_derived_properties()
    {
        var model = new AppModel { Auth = "azuread" };
        model.ExternalProviderAddMethod.Should().Be("AddAzureAd");
        model.ExternalProviderConfigKey.Should().Be("Identity:ExternalProviders:AzureAd");
        model.IdentityConfigJson.Should().Contain("TenantId");
        model.IdentityConfigJson.Should().Contain("Instance");
    }

    [Fact]
    public void External_provider_properties_empty_for_none()
    {
        var model = new AppModel { Auth = "none" };
        model.ExternalProviderName.Should().BeEmpty();
        model.ExternalProviderAddMethod.Should().BeEmpty();
        model.ExternalProviderNamespace.Should().BeEmpty();
        model.IdentityConfigJson.Should().BeEmpty();
    }

}
