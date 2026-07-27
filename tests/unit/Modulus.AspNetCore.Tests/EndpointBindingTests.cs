using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AspNetCore.Endpoints;
using Xunit;

namespace Modulus.AspNetCore.Tests;

// Regression tests for EndpointDiscovery.BindRequestAsync. The defended
// behaviours: (1) a matched route/query property that fails conversion is a
// 400 problem response, never a silently-skipped default (a malformed Guid id
// previously ran the handler against Guid.Empty); (2) malformed JSON bodies
// short-circuit with Succeeded=false instead of falling through to the
// handler; (3) every failure is an RFC 7807 problem — the single framework
// error contract.
[Trait("Category", "Unit")]
public sealed class EndpointBindingTests
{
    private sealed class GetOrderRequest
    {
        public Guid Id { get; set; }
        public int Page { get; set; } = 1;
        public bool IncludeArchived { get; set; }
        public string? Search { get; set; }
    }

    private sealed class CreateOrderRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    private static DefaultHttpContext NewContext()
    {
        var ctx = new DefaultHttpContext
        {
            // A real host always has ILoggerFactory registered; ProblemDetails
            // results need it.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static JsonElement ReadProblem(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8, leaveOpen: true);
        return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
    }

    [Fact]
    public async Task Binds_valid_route_and_query_values()
    {
        var ctx = NewContext();
        var id = Guid.NewGuid();
        ctx.Request.Method = "GET";
        ctx.Request.RouteValues["id"] = id.ToString();
        ctx.Request.QueryString = new QueryString("?page=3&includeArchived=true&search=abc");

        var (request, succeeded) = await EndpointDiscovery.BindRequestAsync(
            typeof(GetOrderRequest), ctx, "GET", CancellationToken.None);

        succeeded.Should().BeTrue();
        var typed = request.Should().BeOfType<GetOrderRequest>().Subject;
        typed.Id.Should().Be(id);
        typed.Page.Should().Be(3);
        typed.IncludeArchived.Should().BeTrue();
        typed.Search.Should().Be("abc");
    }

    [Fact]
    public async Task Malformed_guid_route_value_is_a_400_problem_not_a_silent_default()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.RouteValues["id"] = "not-a-guid";

        var (_, succeeded) = await EndpointDiscovery.BindRequestAsync(
            typeof(GetOrderRequest), ctx, "GET", CancellationToken.None);

        succeeded.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = ReadProblem(ctx);
        problem.GetProperty("errors").GetProperty("Id")[0].GetString()
            .Should().Contain("not-a-guid");
    }

    [Fact]
    public async Task All_bad_parameters_are_reported_at_once()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.QueryString = new QueryString("?page=abc&includeArchived=banana");

        var (_, succeeded) = await EndpointDiscovery.BindRequestAsync(
            typeof(GetOrderRequest), ctx, "GET", CancellationToken.None);

        succeeded.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var errors = ReadProblem(ctx).GetProperty("errors");
        errors.TryGetProperty("Page", out _).Should().BeTrue();
        errors.TryGetProperty("IncludeArchived", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unrecognised_bool_token_is_rejected_not_coerced_to_false()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.QueryString = new QueryString("?includeArchived=banana");

        var (_, succeeded) = await EndpointDiscovery.BindRequestAsync(
            typeof(GetOrderRequest), ctx, "GET", CancellationToken.None);

        succeeded.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Unknown_query_keys_are_ignored()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.QueryString = new QueryString("?utm_source=newsletter&page=2");

        var (request, succeeded) = await EndpointDiscovery.BindRequestAsync(
            typeof(GetOrderRequest), ctx, "GET", CancellationToken.None);

        succeeded.Should().BeTrue();
        request.Should().BeOfType<GetOrderRequest>()
            .Which.Page.Should().Be(2);
    }

    [Fact]
    public async Task Malformed_json_body_short_circuits_with_a_400_problem()
    {
        var ctx = NewContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream("{ not json"u8.ToArray());

        var (_, succeeded) = await EndpointDiscovery.BindRequestAsync(
            typeof(CreateOrderRequest), ctx, "POST", CancellationToken.None);

        succeeded.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ReadProblem(ctx).GetProperty("detail").GetString()
            .Should().Be("Malformed JSON body.");
    }

    [Fact]
    public async Task Problem_responses_carry_the_trace_id()
    {
        var ctx = NewContext();
        ctx.TraceIdentifier = "trace-123";
        ctx.Request.Method = "GET";
        ctx.Request.RouteValues["id"] = "nope";

        await EndpointDiscovery.BindRequestAsync(
            typeof(GetOrderRequest), ctx, "GET", CancellationToken.None);

        ReadProblem(ctx).GetProperty("traceId").GetString()
            .Should().Be("trace-123");
    }
}
