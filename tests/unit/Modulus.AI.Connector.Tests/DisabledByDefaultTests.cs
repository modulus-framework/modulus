namespace Modulus.AI.Connector.Tests;

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Execution;
using Modulus.AI.Connector.Revocation;
using Modulus.Core.Abstractions;
using Xunit;

/// <summary>
/// The connector is off until an operator turns it on, after the platform's conformance suite passes (item 11). Mapping the routes is not
/// activation: while off, no route reads data, and nothing is sent to the platform, even when instances and platform settings are present.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DisabledByDefaultTests
{
    private const string Off = "false";

    [Fact]
    public void The_options_default_to_off()
        => new ModulusAiConnectorOptions().Enabled.Should().BeFalse();

    [Fact]
    public async Task No_route_is_mapped_and_the_platform_is_never_called_while_off()
    {
        // No connector route exists, so the request falls to the host's fallback: an anonymous caller is challenged (401) and a
        // signed-in caller gets 404. Neither reaches the connector, the signing-key fetch or the platform.
        await using var host = await ConnectorTestHost.StartAsync(s => s["Ai:Connector:Enabled"] = Off);

        var requests = new[]
        {
            host.Request(HttpMethod.Get, "/manifest", envelope: null),
            host.Request(HttpMethod.Get, "/extract?appInstanceId=" + ConnectorTestHost.Instance, envelope: null),
            host.Request(HttpMethod.Post, "/capabilities/Test.Catalog.Product.Search:execute", envelope: null, json: "{\"args\":{}}"),
        };
        foreach (var request in requests)
        {
            var response = await host.Client.SendAsync(request);
            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.NotFound, HttpStatusCode.Unauthorized],
                because: request.RequestUri!.AbsolutePath);
        }

        host.Platform.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task An_access_change_queues_no_revocation_while_off()
    {
        await using var host = await ConnectorTestHost.StartAsync(s => s["Ai:Connector:Enabled"] = Off);
        using var scope = host.Services.CreateScope();

        var observers = scope.ServiceProvider.GetServices<IAccessChangeObserver>().OfType<AiRevocationObserver>().ToList();
        observers.Should().ContainSingle("the revocation observer is registered either way");
        await observers[0].OnAccessChangedAsync(new AccessChange { Kind = AccessChangeKinds.Role, Reason = "role.changed" });

        host.Services.GetRequiredService<AiRevocationQueue>().Count.Should().Be(0);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        host.Platform.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task A_signal_journalled_before_activation_is_held_while_off()
    {
        await using var host = await ConnectorTestHost.StartAsync(s => s["Ai:Connector:Enabled"] = Off);

        host.Services.GetRequiredService<AiRevocationQueue>().Enqueue(
            new RevocationSignal(RevocationKeys.For(ConnectorTestHost.Instance), ConnectorTestHost.Instance, "membership.removed", DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        host.Platform.Received.Should().BeEmpty("the dispatcher does not run while the connector is off");
        host.Services.GetRequiredService<AiRevocationQueue>().Count.Should().Be(1, "the signal waits for activation");
    }

    [Fact]
    public async Task The_same_signal_is_sent_once_the_connector_is_on()
    {
        // The control: proves the test above would fail if the dispatcher ignored the switch.
        await using var host = await ConnectorTestHost.StartAsync();

        host.Services.GetRequiredService<AiRevocationQueue>().Enqueue(
            new RevocationSignal(RevocationKeys.For(ConnectorTestHost.Instance), ConnectorTestHost.Instance, "membership.removed", DateTimeOffset.UtcNow));

        (await host.Platform.Succeeded.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        host.Platform.Received.Should().ContainSingle(r => r.Path == "/revocations/scope");
    }
}
