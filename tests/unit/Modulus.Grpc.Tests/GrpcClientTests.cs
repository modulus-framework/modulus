namespace Modulus.Grpc.Tests;

using FluentAssertions;
using global::Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Grpc.Tests.Protos;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GrpcClientTests
{
    [Fact]
    public async Task The_correlation_id_travels_with_the_call()
    {
        await using var host = await GrpcTestHost.StartAsync();
        await using var provider = ClientServices(host);
        var correlation = provider.GetRequiredService<ICorrelationContext>();

        EchoReply reply;
        using (correlation.BeginScope("corr-42"))
            reply = await provider.GetRequiredService<Probe.ProbeClient>().EchoAsync(new EchoRequest { Text = "hi" });

        reply.CorrelationId.Should().Be("corr-42");
    }

    [Fact]
    public async Task The_tenant_is_sent_when_asked()
    {
        await using var host = await GrpcTestHost.StartAsync();
        var tenant = Guid.NewGuid();
        await using var provider = ClientServices(host, configure: b => b.PropagateTenant(),
            services: s => s.AddSingleton<ICurrentTenant>(new FixedTenant(tenant)));

        var reply = await provider.GetRequiredService<Probe.ProbeClient>().EchoAsync(new EchoRequest());

        reply.Tenant.Should().Be(tenant.ToString());
    }

    [Fact]
    public async Task The_incoming_access_token_is_forwarded_when_asked()
    {
        await using var host = await GrpcTestHost.StartAsync();
        await using var provider = ClientServices(host, configure: b => b.ForwardAccessToken());
        var incoming = new DefaultHttpContext();
        incoming.Request.Headers.Authorization = "Bearer token-1";
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = incoming;

        var reply = await provider.GetRequiredService<Probe.ProbeClient>().EchoAsync(new EchoRequest());

        reply.Authorization.Should().Be("Bearer token-1");
    }

    [Fact]
    public async Task Unary_calls_get_the_default_deadline()
    {
        await using var host = await GrpcTestHost.StartAsync();
        await using var provider = ClientServices(host, new Dictionary<string, string?> { ["Grpc:Client:DefaultDeadline"] = "00:00:00.2" });

        var started = DateTime.UtcNow;
        var call = () => provider.GetRequiredService<Probe.ProbeClient>().SlowAsync(new EchoRequest()).ResponseAsync;

        // Either the client's deadline timer or the server's cancellation response can win the race
        // under load; both mean the default deadline ended the call.
        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode
            .Should().BeOneOf(StatusCode.DeadlineExceeded, StatusCode.Cancelled);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Unavailable_is_retried()
    {
        await using var host = await GrpcTestHost.StartAsync();
        host.State.FailuresBeforeSuccess = 2;
        await using var provider = ClientServices(host, new Dictionary<string, string?> { ["Grpc:Client:InitialBackoff"] = "00:00:00.01" });

        var reply = await provider.GetRequiredService<Probe.ProbeClient>().FlakyAsync(new EchoRequest { Text = "ok" });

        reply.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task Retries_can_be_turned_off()
    {
        await using var host = await GrpcTestHost.StartAsync();
        host.State.FailuresBeforeSuccess = 1;
        await using var provider = ClientServices(host, new Dictionary<string, string?> { ["Grpc:Client:MaxAttempts"] = "1" });

        var call = () => provider.GetRequiredService<Probe.ProbeClient>().FlakyAsync(new EchoRequest()).ResponseAsync;

        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unavailable);
        host.State.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Other_failures_are_not_retried()
    {
        await using var host = await GrpcTestHost.StartAsync();
        await using var provider = ClientServices(host);

        var call = () => provider.GetRequiredService<Probe.ProbeClient>().FailAsync(new FailRequest { Kind = "boom" }).ResponseAsync;

        (await call.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Internal);
    }

    private static ServiceProvider ClientServices(
        GrpcTestHost host,
        IDictionary<string, string?>? settings = null,
        Action<IHttpClientBuilder>? configure = null,
        Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        services?.Invoke(collection);
        var builder = collection.AddModulusGrpcClient<Probe.ProbeClient>(host.Server.BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => host.Server.CreateHandler());
        configure?.Invoke(builder);
        return collection.BuildServiceProvider();
    }

    private sealed class FixedTenant(Guid id) : ICurrentTenant
    {
        public Guid? TenantId => id;

        public string? TenantSlug => "acme";

        public bool IsAvailable => true;

        public bool IsHost => false;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
