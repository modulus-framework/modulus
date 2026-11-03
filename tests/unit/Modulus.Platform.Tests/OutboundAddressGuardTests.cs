using FluentAssertions;
using Modulus.Networking;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>Outbound calls must not be aimed at this machine or the private network (SSRF).</summary>
[Trait("Category", "Unit")]
public sealed class OutboundAddressGuardTests
{
    [Theory]
    [InlineData("https://localhost/hook")]
    [InlineData("https://app.localhost/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.1.2.3/hook")]
    [InlineData("https://192.168.0.5/hook")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://[::1]/hook")]
    public void Reserved_hosts_are_recognised_without_dns(string url)
        => OutboundAddressGuard.IsReservedHost(new Uri(url)).Should().BeTrue();

    [Theory]
    [InlineData("https://rp.example.test/logout")]
    [InlineData("https://8.8.8.8/hook")]
    public void Public_hosts_pass_the_literal_check_and_are_checked_at_connect_time(string url)
        => OutboundAddressGuard.IsReservedHost(new Uri(url)).Should().BeFalse();

    [Theory]
    // 6to4 (2002::/16) embedding a private, loopback or metadata IPv4 address
    [InlineData("2002:0a00:0001::1")]          // 10.0.0.1
    [InlineData("2002:7f00:0001::1")]          // 127.0.0.1
    [InlineData("2002:a9fe:a9fe::1")]          // 169.254.169.254
    [InlineData("2002:c0a8:0001::1")]          // 192.168.0.1
    // Teredo (2001:0::/32) whose obfuscated client address is private (bytes inverted)
    [InlineData("2001:0:4136:e378:8000:63bf:3f57:fffe")] // obfuscated client 192.168.0.1 (c0a8:0001 inverted = 3f57:fffe)
    // IPv4-compatible (::/96) private addresses
    [InlineData("::10.0.0.1")]
    [InlineData("::127.0.0.1")]
    // NAT64 with a private IPv4 address
    [InlineData("64:ff9b::0a00:0001")]
    public void Embedded_private_IPv4_is_refused_in_tunnelling_forms(string literal)
        => OutboundAddressGuard.IsPublic(System.Net.IPAddress.Parse(literal)).Should().BeFalse();

    [Theory]
    [InlineData("2002:0808:0808::1")]          // 6to4 carrying 8.8.8.8
    [InlineData("64:ff9b::0808:0808")]         // NAT64 carrying 8.8.8.8
    public void Embedded_public_IPv4_is_allowed_in_tunnelling_forms(string literal)
        => OutboundAddressGuard.IsPublic(System.Net.IPAddress.Parse(literal)).Should().BeTrue();
}
