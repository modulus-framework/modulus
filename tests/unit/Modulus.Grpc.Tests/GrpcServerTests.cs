namespace Modulus.Grpc.Tests;

using FluentAssertions;
using global::Grpc.Core;
using global::Grpc.Health.V1;
using global::Grpc.Net.Client;
using global::Grpc.Reflection.V1Alpha;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modulus.AspNetCore.HealthChecks;
using Modulus.Core.Abstractions;
using Modulus.Grpc.Tests.Protos;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GrpcServerTests
{
    [Fact]
    public async Task Validation_errors_arrive_as_invalid_argument_with_field_violations()
    {
        await using var host = await GrpcTestHost.StartAsync();
        var client = new Probe.ProbeClient(Channel(host));

        var call = () => client.FailAsync(new FailRequest { Kind = "validation" }).ResponseAsync;

        var ex = (await call.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.InvalidArgument);
        ex.Status.Detail.Should().Be("Validation failed");
        ex.GetErrorReason().Should().Be("VALIDATION_FAILED");
        // Field paths are the proto field names (snake_case), not the C# property names.
        ex.GetValidationErrors().Should().Equal("name: must not be empty", "price is too low");
    }

    [Theory]
    [InlineData("notfound", StatusCode.NotFound, "NOT_FOUND")]
    [InlineData("forbidden", StatusCode.PermissionDenied, "PERMISSION_DENIED")]
    [InlineData("conflict", StatusCode.Aborted, "CONFLICT")]
    [InlineData("feature", StatusCode.NotFound, "FEATURE_DISABLED")]
    public async Task Domain_exceptions_map_to_status_codes(string kind, StatusCode code, string reason)
    {
        await using var host = await GrpcTestHost.StartAsync();
        var client = new Probe.ProbeClient(Channel(host));

        var call = () => client.FailAsync(new FailRequest { Kind = kind }).ResponseAsync;

        var ex = (await call.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(code);
        ex.GetErrorReason().Should().Be(reason);
        if (kind == "feature")
            ex.GetErrorMetadata().Should().Contain("feature", "Exports");
    }

    [Fact]
    public async Task An_rpc_exception_thrown_on_purpose_passes_through()
    {
        await using var host = await GrpcTestHost.StartAsync();
        var client = new Probe.ProbeClient(Channel(host));

        var call = () => client.FailAsync(new FailRequest { Kind = "rpc" }).ResponseAsync;

        var ex = (await call.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.ResourceExhausted);
        ex.Status.Detail.Should().Be("slow down");
    }

    [Fact]
    public async Task An_unexpected_error_is_internal_and_hides_the_exception_text()
    {
        await using var host = await GrpcTestHost.StartAsync();
        var client = new Probe.ProbeClient(Channel(host));

        var call = () => client.FailAsync(new FailRequest { Kind = "boom" }).ResponseAsync;

        var ex = (await call.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.Internal);
        ex.Status.Detail.Should().Be("An unexpected error occurred");
        ex.ToString().Should().NotContain("secret");
    }

    [Fact]
    public async Task Detailed_errors_show_the_exception_text()
    {
        await using var host = await GrpcTestHost.StartAsync(new Dictionary<string, string?> { ["Grpc:EnableDetailedErrors"] = "true" });
        var client = new Probe.ProbeClient(Channel(host));

        var call = () => client.FailAsync(new FailRequest { Kind = "boom" }).ResponseAsync;

        (await call.Should().ThrowAsync<RpcException>()).Which.Status.Detail.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task Streaming_calls_map_exceptions_too()
    {
        await using var host = await GrpcTestHost.StartAsync();
        var client = new Probe.ProbeClient(Channel(host));

        using var call = client.Count();
        await call.RequestStream.WriteAsync(new EchoRequest { Text = "a" });
        await call.RequestStream.WriteAsync(new EchoRequest { Text = "boom" });
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(async () => await call);
        ex.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public void Service_discovery_finds_only_grpc_service_implementations()
    {
        GrpcServerExtensions.FindServiceTypes([typeof(ProbeService).Assembly])
            .Should().Equal(typeof(GuardedService), typeof(ProbeService));
    }

    [Fact]
    public async Task Authorization_applies_to_grpc_services()
    {
        await using var host = await GrpcTestHost.StartAsync();
        var client = new Guarded.GuardedClient(Channel(host));

        var anonymous = () => client.EchoAsync(new EchoRequest()).ResponseAsync;
        (await anonymous.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);

        var reply = await client.EchoAsync(new EchoRequest(), new Metadata { { "x-test-user", "alice" } });
        reply.Text.Should().Be("alice");
    }

    [Fact]
    public async Task Correlation_id_metadata_reaches_the_service()
    {
        await using var host = await GrpcTestHost.StartAsync();
        var client = new Probe.ProbeClient(Channel(host));

        var reply = await client.EchoAsync(new EchoRequest { Text = "hi" }, new Metadata { { "x-correlation-id", "abc-123" } });

        reply.CorrelationId.Should().Be("abc-123");
    }

    [Fact]
    public async Task Health_reports_module_health_checks()
    {
        await using var host = await GrpcTestHost.StartAsync();
        var client = new Health.HealthClient(Channel(host));

        (await client.CheckAsync(new HealthCheckRequest())).Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);

        host.State.Health = Modulus.Core.Abstractions.HealthStatus.Degraded;
        (await client.CheckAsync(new HealthCheckRequest())).Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);

        host.State.Health = Modulus.Core.Abstractions.HealthStatus.Unhealthy;
        (await client.CheckAsync(new HealthCheckRequest())).Status.Should().Be(HealthCheckResponse.Types.ServingStatus.NotServing);
    }

    [Fact]
    public async Task Reflection_is_off_unless_enabled()
    {
        await using (var host = await GrpcTestHost.StartAsync())
        {
            var act = () => ListServicesAsync(host);
            (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unimplemented);
        }

        await using (var host = await GrpcTestHost.StartAsync(new Dictionary<string, string?> { ["Grpc:EnableReflection"] = "true" }))
        {
            (await ListServicesAsync(host)).Should().Contain(["modulus.tests.Probe", "modulus.tests.Guarded", "grpc.health.v1.Health"]);
        }
    }

    [Fact]
    public async Task Module_health_checks_can_be_added_twice()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IModuleHealthCheck, ProbeState>();
        services.AddHealthChecks().AddModulusHealthChecks();
        services.AddHealthChecks().AddModulusHealthChecks();
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries.Should().ContainSingle();
        report.Status.Should().Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
    }

    private static async Task<IReadOnlyList<string>> ListServicesAsync(GrpcTestHost host)
    {
        var client = new ServerReflection.ServerReflectionClient(Channel(host));
        using var call = client.ServerReflectionInfo();
        await call.RequestStream.WriteAsync(new ServerReflectionRequest { ListServices = "" });
        await call.RequestStream.CompleteAsync();
        var names = new List<string>();
        await foreach (var response in call.ResponseStream.ReadAllAsync())
            names.AddRange(response.ListServicesResponse.Service.Select(s => s.Name));
        return names;
    }

    internal static GrpcChannel Channel(GrpcTestHost host)
        => GrpcChannel.ForAddress(host.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = host.Server.CreateHandler() });
}
