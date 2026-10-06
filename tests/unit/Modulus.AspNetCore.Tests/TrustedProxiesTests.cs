using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Modulus.AspNetCore.Security;
using Xunit;

namespace Modulus.AspNetCore.Tests;

[Trait("Category", "Unit")]
public sealed class TrustedProxiesTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).Build();

    [Fact]
    public void With_nothing_configured_no_extra_proxy_is_trusted()
    {
        var options = new ForwardedHeadersOptions().ApplyModulusTrustedProxies(Config());

        // The framework default (loopback only) stays: a header from anywhere else is ignored.
        options.KnownProxies.Should().BeEquivalentTo([IPAddress.IPv6Loopback]);
        options.KnownIPNetworks.Select(n => n.ToString()).Should().BeEquivalentTo(["127.0.0.0/8"]);
        options.ForwardLimit.Should().Be(1);
    }

    [Fact]
    public void Named_proxies_and_networks_are_trusted()
    {
        var options = new ForwardedHeadersOptions().ApplyModulusTrustedProxies(Config(
            ("ForwardedHeaders:KnownProxies:0", "203.0.113.7"),
            ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"),
            ("ForwardedHeaders:ForwardLimit", "2")));

        options.KnownProxies.Should().Contain(IPAddress.Parse("203.0.113.7"));
        options.KnownIPNetworks.Select(n => n.ToString()).Should().Contain("10.0.0.0/8");
        options.ForwardLimit.Should().Be(2);
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0")]
    public void A_malformed_entry_fails_startup_instead_of_silently_trusting_nothing_or_everything(string key, string value)
    {
        var apply = () => new ForwardedHeadersOptions().ApplyModulusTrustedProxies(Config((key, value)));

        apply.Should().Throw<InvalidOperationException>().WithMessage("*ForwardedHeaders*");
    }
}
