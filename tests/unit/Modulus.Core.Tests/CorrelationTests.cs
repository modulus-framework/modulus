using System.Net;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;
using Modulus.Core.Http;
using FluentAssertions;
using Xunit;

namespace Modulus.Core.Tests;

[Trait("Category", "Unit")]
public sealed class CorrelationTests
{
    [Fact]
    public void Context_IsUnset_ByDefault()
    {
        var ctx = new CorrelationContext();
        ctx.IsSet.Should().BeFalse();
        ctx.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void BeginScope_SetsAndRestores()
    {
        var ctx = new CorrelationContext();

        using (ctx.BeginScope("abc"))
        {
            ctx.IsSet.Should().BeTrue();
            ctx.CorrelationId.Should().Be("abc");

            using (ctx.BeginScope("nested"))
                ctx.CorrelationId.Should().Be("nested");

            ctx.CorrelationId.Should().Be("abc"); // restored after nested scope
        }

        ctx.IsSet.Should().BeFalse(); // restored after outer scope
    }

    [Fact]
    public void BeginScope_RejectsBlank()
    {
        var ctx = new CorrelationContext();
        Assert.Throws<ArgumentException>(() => ctx.BeginScope("  "));
    }

    [Fact]
    public async Task Handler_AddsCorrelationHeader_WhenSet()
    {
        var ctx = new CorrelationContext();
        var capture = new CapturingHandler();
        var handler = new CorrelationIdPropagationHandler(ctx) { InnerHandler = capture };
        using var invoker = new HttpMessageInvoker(handler);

        using (ctx.BeginScope("req-123"))
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://svc/x");
            await invoker.SendAsync(req, CancellationToken.None);
        }

        capture.SeenHeader.Should().Be("req-123");
    }

    [Fact]
    public async Task Handler_NoHeader_WhenUnset()
    {
        var ctx = new CorrelationContext();
        var capture = new CapturingHandler();
        var handler = new CorrelationIdPropagationHandler(ctx) { InnerHandler = capture };
        using var invoker = new HttpMessageInvoker(handler);

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://svc/x");
        await invoker.SendAsync(req, CancellationToken.None);

        capture.SeenHeader.Should().BeNull();
    }

    [Fact]
    public async Task Handler_DoesNotOverwrite_ExistingHeader()
    {
        var ctx = new CorrelationContext();
        var capture = new CapturingHandler();
        var handler = new CorrelationIdPropagationHandler(ctx) { InnerHandler = capture };
        using var invoker = new HttpMessageInvoker(handler);

        using (ctx.BeginScope("from-context"))
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://svc/x");
            req.Headers.TryAddWithoutValidation(CorrelationHeaders.Default, "caller-set");
            await invoker.SendAsync(req, CancellationToken.None);
        }

        capture.SeenHeader.Should().Be("caller-set");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? SeenHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SeenHeader = request.Headers.TryGetValues(CorrelationHeaders.Default, out var v)
                ? v.First()
                : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
