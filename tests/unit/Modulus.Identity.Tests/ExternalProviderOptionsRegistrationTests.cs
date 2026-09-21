using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Auth0;
using Modulus.Identity.Authentik;
using Modulus.Identity.AzureAd;
using Modulus.Identity.Duende;
using Modulus.Identity.Keycloak;
using Modulus.Identity.Okta;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Regression: every <c>AddXxx</c> adapter registers its provider with a raw
/// options constructor parameter (e.g. <c>AuthentikIdentityProvider(HttpClient,
/// AuthentikOptions)</c>). The bound options value must therefore also be
/// registered, otherwise scoped activation fails container validation in
/// Development (and fails at runtime whenever <see
/// cref="IExternalIdentityProvider"/> is resolved).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExternalProviderOptionsRegistrationTests
{
    private static IConfiguration Config(string section, Dictionary<string, string?> values)
    {
        var prefixed = values.ToDictionary(
            kvp => $"Identity:ExternalProviders:{section}:{kvp.Key}",
            kvp => kvp.Value);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(prefixed)
            .Build();
    }

    private static void ShouldResolveProvider(
        IConfiguration config,
        Func<AuthenticationBuilder, AuthenticationBuilder> addProvider)
    {
        var services = new ServiceCollection();
        addProvider(services.AddAuthentication());

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider
            .GetRequiredService<IExternalIdentityProvider>();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddAuthentik_RegistersResolvableProvider()
    {
        var config = Config("Authentik", new Dictionary<string, string?>
        {
            ["Authority"] = "https://auth.example.com/",
            ["ClientId"] = "meetup",
            ["Audience"] = "meetup",
        });

        ShouldResolveProvider(config, b => b.AddAuthentik(config));
    }

    [Fact]
    public void AddAuth0_RegistersResolvableProvider()
    {
        var config = Config("Auth0", new Dictionary<string, string?>
        {
            ["Authority"] = "https://auth.example.com/",
            ["ClientId"] = "client",
        });

        ShouldResolveProvider(config, b => b.AddAuth0(config));
    }

    [Fact]
    public void AddOkta_RegistersResolvableProvider()
    {
        var config = Config("Okta", new Dictionary<string, string?>
        {
            ["Authority"] = "https://auth.example.com/",
            ["ClientId"] = "client",
        });

        ShouldResolveProvider(config, b => b.AddOkta(config));
    }

    [Fact]
    public void AddAzureAd_RegistersResolvableProvider()
    {
        var config = Config("AzureAd", new Dictionary<string, string?>
        {
            ["Instance"] = "https://login.example.com/",
            ["TenantId"] = "tenant",
            ["ClientId"] = "client",
        });

        ShouldResolveProvider(config, b => b.AddAzureAd(config));
    }

    [Fact]
    public void AddDuendeIdentityServer_RegistersResolvableProvider()
    {
        var config = Config("Duende", new Dictionary<string, string?>
        {
            ["Authority"] = "https://auth.example.com/",
            ["ClientId"] = "client",
        });

        ShouldResolveProvider(config, b => b.AddDuendeIdentityServer(config));
    }

    [Fact]
    public void AddKeycloak_RegistersResolvableProvider()
    {
        var config = Config("Keycloak", new Dictionary<string, string?>
        {
            ["Authority"] = "https://auth.example.com/",
            ["Realm"] = "realm",
            ["ClientId"] = "client",
        });

        ShouldResolveProvider(config, b => b.AddKeycloak(config));
    }
}
