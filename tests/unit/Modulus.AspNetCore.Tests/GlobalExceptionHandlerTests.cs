using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.AspNetCore.Middleware;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.AspNetCore.Tests;

[Trait("Category", "Unit")]
public sealed class GlobalExceptionHandlerTests
{
    private static async Task<(int Status, JsonElement Body)> RunAsync(Exception exception)
    {
        var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    [Fact]
    public async Task A_cross_tenant_write_is_403_with_the_shared_code()
    {
        var (status, body) = await RunAsync(new CrossTenantWriteException("Product", Guid.NewGuid(), Guid.NewGuid()));

        status.Should().Be(StatusCodes.Status403Forbidden);
        body.GetProperty("code").GetString().Should().Be("PERMISSION_DENIED");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Validation_errors_use_the_dictionary_shape_of_endpoint_validation()
    {
        var (status, body) = await RunAsync(new ValidationException(["Name: required", "Name: too short", "Price: positive"]));

        status.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
        body.GetProperty("errors").GetProperty("Name").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("required", "too short");
        body.GetProperty("errors").GetProperty("Price")[0].GetString().Should().Be("positive");
    }

    [Fact]
    public async Task An_oversized_body_is_413_not_500()
    {
        var (status, body) = await RunAsync(
            new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge));

        status.Should().Be(StatusCodes.Status413PayloadTooLarge);
        body.GetProperty("code").GetString().Should().Be("BAD_REQUEST");
    }

    [Fact]
    public async Task Unknown_failures_are_500_without_leaking_the_message()
    {
        var (status, body) = await RunAsync(new InvalidOperationException("password=hunter2"));

        status.Should().Be(StatusCodes.Status500InternalServerError);
        body.GetRawText().Should().NotContain("hunter2");
        body.GetProperty("code").GetString().Should().Be("INTERNAL");
    }
}
