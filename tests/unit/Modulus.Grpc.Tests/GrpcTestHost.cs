namespace Modulus.Grpc.Tests;

using System.Security.Claims;
using System.Text.Encodings.Web;
using global::Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Correlation;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Grpc.Tests.Protos;

/// <summary>A gRPC host on a TestServer, with correlation, a header-based test sign-in and one module health check.</summary>
internal sealed class GrpcTestHost : IAsyncDisposable
{
    private GrpcTestHost(WebApplication app) => App = app;

    public WebApplication App { get; }

    public TestServer Server => App.GetTestServer();

    public ProbeState State => App.Services.GetRequiredService<ProbeState>();

    public static async Task<GrpcTestHost> StartAsync(IDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());

        builder.Services.AddModulusCorrelation(builder.Configuration);
        builder.Services.AddModulusGrpc(builder.Configuration);
        builder.Services.AddSingleton<ProbeState>();
        builder.Services.AddSingleton<ProbeStateAccessor>();
        builder.Services.AddSingleton<IModuleHealthCheck>(sp => sp.GetRequiredService<ProbeState>());
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseModulusCorrelation();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapModulusGrpc(typeof(ProbeService).Assembly);
        await app.StartAsync();
        return new GrpcTestHost(app);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

/// <summary>Shared test state: the module health status and the flaky call's attempt counter.</summary>
internal sealed class ProbeState : IModuleHealthCheck
{
    private int _attempts;

    public HealthStatus Health { get; set; } = HealthStatus.Healthy;

    public int FailuresBeforeSuccess { get; set; }

    public int Attempts => _attempts;

    public int NextAttempt() => Interlocked.Increment(ref _attempts);

    public Task<ModuleHealthResult> CheckAsync(CancellationToken ct = default)
        => Task.FromResult(new ModuleHealthResult("Probe", Health, Health.ToString(), TimeSpan.Zero));
}

/// <summary>Signs a caller in when the <c>x-test-user</c> header is present.</summary>
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["x-test-user"].ToString();
        if (user.Length == 0)
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed class ProbeService(ICorrelationContext correlation, ProbeStateAccessor state) : Probe.ProbeBase
{
    public override Task<EchoReply> Echo(EchoRequest request, ServerCallContext context)
        => Task.FromResult(new EchoReply
        {
            Text = request.Text,
            CorrelationId = correlation.CorrelationId ?? "",
            Tenant = context.RequestHeaders.GetValue("x-tenant-id") ?? "",
            Authorization = context.RequestHeaders.GetValue("authorization") ?? "",
        });

    public override Task<EchoReply> Fail(FailRequest request, ServerCallContext context) => request.Kind switch
    {
        "validation" => throw new ValidationException(["Name: must not be empty", "price is too low"]),
        "notfound" => throw new NotFoundException("Product 42 was not found"),
        "forbidden" => throw new ForbiddenException("catalog:products:manage"),
        "conflict" => throw new ConflictException("Name already taken"),
        "feature" => throw new FeatureDisabledException("Exports"),
        "rpc" => throw new RpcException(new Status(StatusCode.ResourceExhausted, "slow down")),
        _ => throw new InvalidOperationException("connection string leaked: Server=db;Password=secret"),
    };

    public override async Task<EchoReply> Slow(EchoRequest request, ServerCallContext context)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken);
        return new EchoReply { Text = request.Text };
    }

    public override Task<EchoReply> Flaky(EchoRequest request, ServerCallContext context)
    {
        var attempt = state.State.NextAttempt();
        if (attempt <= state.State.FailuresBeforeSuccess)
            throw new RpcException(new Status(StatusCode.Unavailable, "warming up"));
        return Task.FromResult(new EchoReply { Text = request.Text, Attempts = attempt });
    }

    public override async Task<EchoReply> Count(IAsyncStreamReader<EchoRequest> requestStream, ServerCallContext context)
    {
        var count = 0;
        await foreach (var item in requestStream.ReadAllAsync(context.CancellationToken))
        {
            if (item.Text == "boom")
                throw new NotFoundException("missing");
            count++;
        }

        return new EchoReply { Attempts = count };
    }
}

[Authorize]
public sealed class GuardedService : Guarded.GuardedBase
{
    public override Task<EchoReply> Echo(EchoRequest request, ServerCallContext context)
        => Task.FromResult(new EchoReply { Text = context.GetHttpContext().User.Identity?.Name ?? "" });
}

/// <summary>Not a gRPC service: must not be mapped.</summary>
public sealed class NotAService;

/// <summary>Lets the public service class take the internal state through DI.</summary>
public sealed class ProbeStateAccessor(IServiceProvider services)
{
    internal ProbeState State => services.GetRequiredService<ProbeState>();
}
