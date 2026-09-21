using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using OpenIddict.Server;
using Xunit;

namespace Modulus.Identity.Tests;

[Trait("Category", "Unit")]
public sealed class ModulusIntrospectionControllerTests
{
    private const string ClientId = "gateway";
    private const string ClientSecret = "s3cret:value";

    [Fact]
    public async Task NoCredentialsConfigured_Returns401_EvenWithBasicHeader()
    {
        var controller = Build(new ModulusIdentityOptions());
        SetBasicAuthorization(controller, ClientId, ClientSecret);

        var result = await controller.Introspect(token: "whatever");

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task BearerTokenCaller_Returns401()
    {
        // The regression: a plain end-user access token must NOT authorize
        // introspection of arbitrary submitted tokens.
        var options = new ModulusIdentityOptions
        {
            IntrospectionClientId = ClientId,
            IntrospectionClientSecret = ClientSecret,
        };
        var controller = Build(options);
        controller.Request.Headers.Authorization = "Bearer some-user-access-token";

        var result = await controller.Introspect(token: "whatever");

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task WrongBasicCredentials_Return401()
    {
        var controller = Build(WithCredentials());
        SetBasicAuthorization(controller, ClientId, "wrong-secret");

        var result = await controller.Introspect(token: "whatever");

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task WrongBasicClientId_Returns401()
    {
        var controller = Build(WithCredentials());
        SetBasicAuthorization(controller, "intruder", ClientSecret);

        var result = await controller.Introspect(token: "whatever");

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task MalformedBasicHeader_Returns401()
    {
        var controller = Build(WithCredentials());
        controller.Request.Headers.Authorization = "Basic !!!not-base64!!!";

        var result = await controller.Introspect(token: "whatever");

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task ValidBasicCredentials_WithGarbageToken_ReturnsInactive()
    {
        var controller = Build(WithCredentials());
        SetBasicAuthorization(controller, ClientId, ClientSecret);

        var result = await controller.Introspect(token: "not-a-jwt");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value!.GetType().GetProperty("active")!.GetValue(ok.Value).Should().Be(false);
    }

    [Fact]
    public async Task SecretContainingColon_IsSplitOnFirstColonOnly()
    {
        var controller = Build(WithCredentials());
        SetBasicAuthorization(controller, ClientId, ClientSecret); // "s3cret:value"

        var result = await controller.Introspect(token: "not-a-jwt");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ValidFormCredentials_WithGarbageToken_ReturnsInactive()
    {
        var controller = Build(WithCredentials());
        controller.Request.ContentType = "application/x-www-form-urlencoded";
        controller.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["token"] = "not-a-jwt",
            });

        var result = await controller.Introspect(token: "not-a-jwt");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task EmptyToken_WithValidCredentials_ReturnsInactive()
    {
        var controller = Build(WithCredentials());
        SetBasicAuthorization(controller, ClientId, ClientSecret);

        var result = await controller.Introspect(token: " ");

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── helpers ────────────────────────────────────────────────────

    private static ModulusIdentityOptions WithCredentials() => new()
    {
        IntrospectionClientId = ClientId,
        IntrospectionClientSecret = ClientSecret,
    };

    private static ModulusIntrospectionController Build(ModulusIdentityOptions identityOptions)
        => new(
            Options.Create(new OpenIddictServerOptions()),
            Options.Create(identityOptions))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    private static void SetBasicAuthorization(
        ControllerBase controller, string clientId, string clientSecret)
        => controller.Request.Headers.Authorization =
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
}
