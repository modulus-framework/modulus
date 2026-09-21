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

    [Fact]
    public void Framework_version_matches_packaging_prefix()
    {
        // Keep cli/Services/Models.cs FrameworkVersion.Current in sync with
        // build/Modulus.Packaging.props <VersionPrefix>.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? propsPath = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "build", "Modulus.Packaging.props");
            if (File.Exists(candidate))
            {
                propsPath = candidate;
                break;
            }
            dir = dir.Parent;
        }
        propsPath.Should().NotBeNull("repo root with build/Modulus.Packaging.props should be discoverable");
        var props = File.ReadAllText(propsPath!);
        props.Should().Contain($"<VersionPrefix>{FrameworkVersion.Current}</VersionPrefix>");
    }

    [Theory]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer", "10.0.9")]
    [InlineData("PostgreSQL", "Npgsql.EntityFrameworkCore.PostgreSQL", "10.0.2")]
    [InlineData("MySQL", "MySql.EntityFrameworkCore", "10.0.7")]
    [InlineData("SQLite", "Microsoft.EntityFrameworkCore.Sqlite", "10.0.9")]
    public void Db_provider_info_matches_central_pins(string provider, string package, string version)
    {
        DbProviderInfo.Package(provider).Should().Be(package);
        DbProviderInfo.Version(provider).Should().Be(version);
    }

}
