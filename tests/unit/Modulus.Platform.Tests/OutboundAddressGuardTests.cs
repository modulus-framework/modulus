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
}
