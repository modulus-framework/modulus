using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public class ModuleDiscoveryTests
{
    [Fact]
    public void DetectEnabledFeatures_returns_empty_when_file_missing()
    {
        var features = ModuleDiscovery.DetectEnabledFeatures("/nonexistent/file.cs");
        features.Should().BeEmpty();
    }

    [Fact]
    public void DetectEnabledFeatures_finds_all_framework_features()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                using Modulus.AspNetCore.Extensions;

                builder.Services.AddModulus(builder.Configuration, modules =>
                {
                    modules.AddModule<AppModule>();
                });
                builder.Services.AddModulusCorrelation(builder.Configuration);
                builder.Services.AddModulusIdempotency(builder.Configuration);
                builder.Services.AddModulusApiVersioning(builder.Configuration);
                builder.Services.AddModulusRateLimiting(builder.Configuration);
                builder.Services.AddModulusCors(builder.Configuration);
                builder.Services.AddModulusSecurityHeaders(builder.Configuration);
                builder.Services.AddModulusFeatureFlags(builder.Configuration);
                builder.Services.AddModulusSecretsGuard(builder.Configuration);
                builder.Services.AddModulusPersonalDataProtection(builder.Configuration);
                builder.Services.AddModulusOpenApi(builder.Configuration);
                builder.Services.AddModulusEvents(typeof(Program).Assembly);
                builder.Services.AddMediator();
                builder.Services.AddControllers();
                builder.Services.Configure<ForwardedHeadersOptions>(_ => { });
                app.MapModulusHealthChecks();
                """);
            var features = ModuleDiscovery.DetectEnabledFeatures(tempFile);
            features.Should().Contain(new[]
            {
                "Correlation", "Idempotency", "API versioning", "Rate limiting",
                "CORS", "Security headers", "Feature flags", "Secrets guard",
                "PII encryption", "OpenAPI", "Health checks", "Forwarded headers",
                "Modulus modules", "Mediator", "Domain events",
            });
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectEnabledFeatures_detects_openiddict_auth()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                builder.Services.AddModulusOpenIddict(builder.Configuration);
                """);
            var features = ModuleDiscovery.DetectEnabledFeatures(tempFile);
            features.Should().Contain("Auth: OpenIddict");
            features.Should().NotContain("Auth: Keycloak");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("AddAuth0(", "Auth: Auth0")]
    [InlineData("AddAuthentik(", "Auth: Authentik")]
    [InlineData("AddAzureAd(", "Auth: Azure AD")]
    [InlineData("AddDuendeIdentityServer(", "Auth: Duende")]
    [InlineData("AddKeycloak(", "Auth: Keycloak")]
    [InlineData("AddOkta(", "Auth: Okta")]
    public void DetectEnabledFeatures_detects_each_external_provider(
        string marker, string expectedLabel)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, $"builder.Services.AddAuthentication().{marker}builder.Configuration);");
            var features = ModuleDiscovery.DetectEnabledFeatures(tempFile);
            features.Should().Contain(expectedLabel);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectEnabledFeatures_returns_empty_for_file_with_no_framework_calls()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                // Just a comment
                var x = 42;
                """);
            var features = ModuleDiscovery.DetectEnabledFeatures(tempFile);
            features.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
