using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Idempotency;
using Xunit;

namespace Modulus.AspNetCore.Tests;

[Trait("Category", "Unit")]
public sealed class IdempotencyMiddlewareTests
{
    [Fact]
    public async Task GetRequest_IsNotGuarded_AndAlwaysRuns()
    {
        var (mw, store, calls) = Build();

        await InvokeAsync(mw, store, "GET", key: "k");
        await InvokeAsync(mw, store, "GET", key: "k");

        calls.Count.Should().Be(2); // both invocations reached the downstream delegate
    }

    [Fact]
    public async Task KeylessPost_PassesThrough_WhenKeyNotRequired()
    {
        var (mw, store, calls) = Build();

        var ctx = await InvokeAsync(mw, store, "POST", key: null);

        calls.Count.Should().Be(1);
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task KeylessPost_Returns400_WhenKeyRequired()
    {
        var (mw, store, calls) = Build(o => o.RequireKey = true);

        var ctx = await InvokeAsync(mw, store, "POST", key: null);

        ctx.Response.StatusCode.Should().Be(400);
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task OverlongKey_Returns400()
    {
        var (mw, store, calls) = Build(o => o.MaxKeyLength = 8);

        var ctx = await InvokeAsync(mw, store, "POST", key: new string('x', 9));

        ctx.Response.StatusCode.Should().Be(400);
        calls.Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicatePost_ReplaysResponse_WithoutReprocessing()
    {
        var (mw, store, calls) = Build(downstream: async ctx =>
        {
            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync("created");
        });

        var first = await InvokeAsync(mw, store, "POST", key: "k", body: "payload");
        var second = await InvokeAsync(mw, store, "POST", key: "k", body: "payload");

        calls.Count.Should().Be(1); // second served from cache
        second.Response.StatusCode.Should().Be(201);
        ReadBody(second).Should().Be("created");
        second.Response.Headers["Idempotency-Replayed"].ToString().Should().Be("true");
        first.Response.Headers.ContainsKey("Idempotency-Replayed").Should().BeFalse();
    }

    [Fact]
    public async Task InFlightKey_Returns409()
    {
        // Disable match-validation so the in-progress path is exercised regardless
        // of the seeded fingerprint.
        var (mw, store, _) = Build(o => o.ValidateRequestMatch = false);
        // Pre-seed an unfinished claim for the same (unscoped) key.
        await store.TryBeginAsync("k", "seed", default);

        var ctx = await InvokeAsync(mw, store, "POST", key: "k", body: "payload");

        ctx.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task KeyReuse_WithDifferentBody_Returns422()
    {
        var (mw, store, _) = Build();

        await InvokeAsync(mw, store, "POST", key: "k", body: "first");
        var reuse = await InvokeAsync(mw, store, "POST", key: "k", body: "second");

        reuse.Response.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task ServerError_IsNotCached_SoRetryReprocesses()
    {
        var status = 500;
        var (mw, store, calls) = Build(downstream: ctx =>
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        });

        var first = await InvokeAsync(mw, store, "POST", key: "k", body: "payload");
        first.Response.StatusCode.Should().Be(500);

        status = 200; // downstream now succeeds
        var second = await InvokeAsync(mw, store, "POST", key: "k", body: "payload");

        calls.Count.Should().Be(2); // claim was released, so the retry ran
        second.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task OversizedResponse_StreamsThroughUncached_AndRetryIsRefusedNotRerun()
    {
        var (mw, store, calls) = Build(
            o => o.MaxResponseBytes = 16,
            downstream: async ctx =>
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync(new string('a', 100));
            });

        var first = await InvokeAsync(mw, store, "POST", key: "k", body: "payload");
        var second = await InvokeAsync(mw, store, "POST", key: "k", body: "payload");

        calls.Count.Should().Be(1); // the side effect ran once; the retry must not repeat it
        first.Response.StatusCode.Should().Be(200);
        ReadBody(first).Should().HaveLength(100); // full body still reached the client
        second.Response.StatusCode.Should().Be(409); // completed, but the response cannot be replayed
        first.Response.Headers.ContainsKey("Idempotency-Replayed").Should().BeFalse();
    }

    [Fact]
    public async Task GrpcCalls_AreNeverGuarded()
    {
        var (mw, store, calls) = Build(o => o.RequireKey = true);

        var ctx = await InvokeAsync(mw, store, "POST", key: null, contentType: "application/grpc");

        calls.Count.Should().Be(1); // not rejected for the missing key
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ExcludedPaths_AreNeverGuarded()
    {
        var (mw, store, calls) = Build(o => o.RequireKey = true);

        await InvokeAsync(mw, store, "POST", key: null, path: "/graphql");
        await InvokeAsync(mw, store, "POST", key: null, path: "/GraphQL/ui");

        calls.Count.Should().Be(2);
    }

    [Fact]
    public async Task AnonymousCallers_DoNotShareAnEntry()
    {
        var (mw, store, calls) = Build();

        await InvokeAsync(mw, store, "POST", key: "k", body: "payload", remoteIp: "10.0.0.1");
        var other = await InvokeAsync(mw, store, "POST", key: "k", body: "payload", remoteIp: "10.0.0.2");

        calls.Count.Should().Be(2); // the second caller was not served the first caller's response
        other.Response.Headers.ContainsKey("Idempotency-Replayed").Should().BeFalse();
    }

    [Fact]
    public async Task ALateCompleter_DoesNotOverwriteAStoredResponse()
    {
        var store = new InMemoryIdempotencyStoreProbe().Create();
        await store.TryBeginAsync("k", "f", default);
        await store.CompleteAsync("k", new CachedResponse(201, new Dictionary<string, string>(), [1]), default);

        await store.CompleteAsync("k", new CachedResponse(500, new Dictionary<string, string>(), [2]), default);

        var result = await store.TryBeginAsync("k", "f", default);
        result.Response!.StatusCode.Should().Be(201);
    }

    private sealed class InMemoryIdempotencyStoreProbe
    {
        public IIdempotencyStore Create() => (IIdempotencyStore)Activator.CreateInstance(
            typeof(IdempotencyOptions).Assembly.GetType("Modulus.AspNetCore.Idempotency.InMemoryIdempotencyStore")!,
            Options.Create(new IdempotencyOptions()), null)!;
    }

    // ── helpers ────────────────────────────────────────────────────

    private static (IdempotencyMiddleware, IIdempotencyStore, List<HttpContext>) Build(
        Action<IdempotencyOptions>? configure = null,
        Func<HttpContext, Task>? downstream = null)
    {
        var options = new IdempotencyOptions();
        configure?.Invoke(options);

        var calls = new List<HttpContext>();
        RequestDelegate next = async ctx =>
        {
            calls.Add(ctx);
            if (downstream is not null)
                await downstream(ctx);
            else
                ctx.Response.StatusCode = 200;
        };

        var store = new InMemoryIdempotencyStore(Options.Create(options));
        var mw = new IdempotencyMiddleware(
            next, Options.Create(options), NullLogger<IdempotencyMiddleware>.Instance);
        return (mw, store, calls);
    }

    private static async Task<HttpContext> InvokeAsync(
        IdempotencyMiddleware mw, IIdempotencyStore store, string method, string? key, string body = "",
        string? contentType = null, string path = "/orders", string? remoteIp = null)
    {
        var ctx = new DefaultHttpContext
        {
            // A real host always has ILoggerFactory registered; ProblemDetails results need it.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.ContentType = contentType;
        if (remoteIp is not null)
            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (key is not null)
            ctx.Request.Headers["Idempotency-Key"] = key;
        ctx.Response.Body = new MemoryStream();

        await mw.InvokeAsync(ctx, store);
        return ctx;
    }

    private static string ReadBody(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
