using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Identity.Extensions;
using OpenIddict.Server;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary><c>Identity:Issuer</c> pins the token server's issuer, so a host with several endpoints accepts its tokens on each.</summary>
[Trait("Category", "Unit")]
public sealed class IdentityIssuerTests
{
    [Theory]
    [InlineData("http://localhost:5180/", "http://localhost:5180/")]
    [InlineData(null, null)]
    public void The_configured_issuer_is_used(string? configured, string? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:UseDevelopmentCertificates"] = "true",
                ["Identity:Issuer"] = configured,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusOpenIddict(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue.Issuer?.ToString().Should().Be(expected);
    }
}
