namespace Modulus.Realtime.Tests;

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Realtime.Delivery;
using Xunit;

[Trait("Category", "Unit")]
public sealed class RealtimeDeliveryTests
{
    private static ServiceProvider Provider(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        return new ServiceCollection().AddLogging().AddModulusRealtime(configuration).BuildServiceProvider();
    }

    private static RealtimeConnection Connection(string id = "c1", string user = "alice", int capacity = 10)
        => new(id, "test", new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user)], "Test")), null, null, capacity);

    private static RealtimeMessage Message(string id, string type = "t", string? user = null)
        => new(id, type, "{}", null, user is null ? null : [user], null, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_client_that_falls_behind_is_closed_instead_of_buffering_without_bound()
    {
        await using var services = Provider();
        var dispatcher = services.GetRequiredService<RealtimeDispatcher>();
        var slow = Connection(capacity: 2);
        dispatcher.TryRegister(slow).Should().BeTrue();

        for (var i = 0; i < 3; i++)
            await dispatcher.DispatchAsync(Message("m" + i));

        slow.IsClosed.Should().BeTrue();
        slow.CloseReason.Should().Be("overflow");
    }

    [Fact]
    public async Task A_redelivered_message_is_delivered_once()
    {
        await using var services = Provider();
        var dispatcher = services.GetRequiredService<RealtimeDispatcher>();
        var connection = Connection();
        dispatcher.TryRegister(connection);

        await dispatcher.DispatchAsync(Message("same"));
        await dispatcher.DispatchAsync(Message("same"));

        connection.Reader.Count.Should().Be(1);
    }

    [Fact]
    public async Task Replay_stops_where_live_delivery_began_and_skips_messages_for_others()
    {
        await using var services = Provider();
        var dispatcher = services.GetRequiredService<RealtimeDispatcher>();
        await dispatcher.DispatchAsync(Message("1"));
        await dispatcher.DispatchAsync(Message("2"));
        await dispatcher.DispatchAsync(Message("3", user: "bob"));
        var connection = Connection();
        dispatcher.TryRegister(connection);
        await dispatcher.DispatchAsync(Message("4"));

        var replay = await dispatcher.ReplayAsync(connection, "1", CancellationToken.None);

        replay.Complete.Should().BeTrue();
        replay.Messages.Select(m => m.Id).Should().Equal("2");
        connection.Reader.TryRead(out var live).Should().BeTrue();
        live!.Id.Should().Be("4");
    }

    [Fact]
    public async Task The_replay_buffer_is_bounded_by_size()
    {
        await using var services = Provider(new() { ["Realtime:ReplayBufferSize"] = "2" });
        var dispatcher = services.GetRequiredService<RealtimeDispatcher>();
        foreach (var id in new[] { "1", "2", "3" })
            await dispatcher.DispatchAsync(Message(id));
        var connection = Connection();
        dispatcher.TryRegister(connection);

        (await dispatcher.ReplayAsync(connection, "1", CancellationToken.None)).Complete.Should().BeFalse("'1' was evicted");
        (await dispatcher.ReplayAsync(connection, "2", CancellationToken.None)).Messages.Select(m => m.Id).Should().Equal("3");
    }

    [Fact]
    public async Task Unregistering_closes_the_connection_and_frees_its_slot()
    {
        await using var services = Provider(new() { ["Realtime:MaxConnectionsPerUser"] = "1" });
        var dispatcher = services.GetRequiredService<RealtimeDispatcher>();
        var first = Connection("a");
        dispatcher.TryRegister(first).Should().BeTrue();
        dispatcher.TryRegister(Connection("b")).Should().BeFalse();

        dispatcher.Unregister(first);

        first.IsClosed.Should().BeTrue();
        dispatcher.TryRegister(Connection("c")).Should().BeTrue();
    }

    [Fact]
    public void Messages_survive_the_backplane_round_trip()
    {
        var message = new RealtimeMessage("01", "catalog.x", "{\"a\":1}", Guid.NewGuid(), ["u1"], "orders:1", "orders:read", DateTimeOffset.UtcNow);

        var copy = RealtimeMessage.FromJson(message.ToJson());

        copy.Should().BeEquivalentTo(message);
        RealtimeMessage.FromJson("not json").Should().BeNull();
        RealtimeMessage.FromJson("{}").Should().BeNull();
    }

    [Fact]
    public void Audiences_and_topics_are_validated()
    {
        RealtimeAudience.Permission("catalog:read").Should().BeEquivalentTo(new { Users = (IReadOnlyList<string>?)null, Topic = (string?)null, RequiredPermission = "catalog:read" });
        RealtimeAudience.ForUsers(["a", "a", " "]).Users.Should().Equal("a");
        RealtimeAudience.User(Guid.Empty).Users.Should().Equal("00000000-0000-0000-0000-000000000000");
        var badTopic = () => RealtimeAudience.ForTopic("orders:*");
        badTopic.Should().Throw<ArgumentException>();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var badPattern = () => services.AddModulusRealtime(configuration, r => r.AddTopic("*"));
        badPattern.Should().Throw<ArgumentException>();
        var reserved = () => services.AddModulusRealtime(configuration, r => r.AddEvent<ReservedEvent>(_ => RealtimeAudience.Tenant));
        reserved.Should().Throw<InvalidOperationException>().WithMessage("*reserved*");
    }

    [Theory]
    [InlineData("true", "Inherited")]
    [InlineData("false", "Anonymous")]
    public void Topics_are_described_for_the_security_guard(string signedIn, string open)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Realtime:RequireAuthenticatedUser"] = signedIn })
            .Build();
        services.AddModulusRealtime(configuration, r => r
            .AddTopic("news")
            .AddTopic("orders:*", "orders:read", _ => ValueTask.FromResult(true)));
        using var provider = services.BuildServiceProvider();

        var entries = provider.GetServices<Modulus.Core.Abstractions.Security.ISecuritySurfaceContributor>()
            .SelectMany(c => c.Describe(provider))
            .ToDictionary(e => e.Name);

        entries["topic orders:*"].Policies.Should().Equal("orders:read", "(callback)");
        entries["topic orders:*"].Access.Should().Be(Modulus.Core.Abstractions.Security.SecuritySurfaceAccess.Policed);
        entries["topic news"].Access.ToString().Should().Be(open);
    }

    [Fact]
    public async Task Publishing_to_an_empty_user_list_sends_nothing()
    {
        await using var services = Provider();
        var dispatcher = services.GetRequiredService<RealtimeDispatcher>();
        var connection = Connection();
        dispatcher.TryRegister(connection);

        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRealtimePublisher>().PublishAsync("t", null, RealtimeAudience.ForUsers([]));

        connection.Reader.Count.Should().Be(0);
    }

    [Fact]
    public async Task Mapping_without_registration_fails_clearly()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        await using var app = builder.Build();

        var map = () => app.MapModulusRealtime();

        map.Should().Throw<InvalidOperationException>().WithMessage("*AddModulusRealtime*");
    }

    [Events.Abstractions.IntegrationEventName("modulus.internal")]
    private sealed record ReservedEvent() : Events.Abstractions.IntegrationEventBase("modulus.internal");
}
