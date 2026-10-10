using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Modulus.SignalR.Tests;

[Trait("Category", "Unit")]
public sealed class SignalRBackplaneExtensionsTests
{
    // Well-formed but not reachable: registration never connects, so this only proves the wiring.
    private const string AzureConnectionString =
        "Endpoint=https://modulus-test.service.signalr.net;AccessKey=VGhpcyBpcyBhIHRlc3Qga2V5;Version=1.0;";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void Redis_backplane_requires_a_connection_string()
    {
        var builder = new ServiceCollection().AddSignalR();

        var act = () => builder.AddRedisBackplane(Config());

        act.Should().Throw<InvalidOperationException>().WithMessage("*SignalR:Redis:ConnectionString*");
    }

    [Fact]
    public void Azure_backplane_requires_a_connection_string()
    {
        var builder = new ServiceCollection().AddSignalR();

        var act = () => builder.AddAzureBackplane(Config());

        act.Should().Throw<InvalidOperationException>().WithMessage("*SignalR:Azure:ConnectionString*");
    }

    [Fact]
    public void Redis_backplane_registers_services_and_returns_the_same_builder()
    {
        var services = new ServiceCollection();
        var builder = services.AddSignalR();
        var before = services.Count;

        var result = builder.AddRedisBackplane(Config(("SignalR:Redis:ConnectionString", "localhost:6379")));

        result.Should().BeSameAs(builder);
        services.Count.Should().BeGreaterThan(before);
    }

    [Fact]
    public void Azure_backplane_registers_services_and_returns_the_same_builder()
    {
        var services = new ServiceCollection();
        var builder = services.AddSignalR();
        var before = services.Count;

        var result = builder.AddAzureBackplane(Config(("SignalR:Azure:ConnectionString", AzureConnectionString)));

        result.Should().BeSameAs(builder);
        services.Count.Should().BeGreaterThan(before);
    }
}
