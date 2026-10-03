using System.Text.Json;
using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// Generated hosts with a sign-in wire the startup security guard, are closed by default (fallback policy) and ship a
/// loosening allow-list that covers the anonymous endpoints the template itself maps.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityGuardTemplateTests
{
    private readonly TemplateEngine _engine = new();

    private static AppModel Model(string auth, AppKind kind = AppKind.Api) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
        Kind = kind,
    };

    [Theory]
    [InlineData("openiddict", "api")]
    [InlineData("keycloak", "api")]
    [InlineData("openiddict", "webapp")]
    [InlineData("openiddict", "webapp+api")]
    public void Hosts_with_a_sign_in_wire_the_guard_and_loosen_the_development_OpenAPI_document(string auth, string kind)
    {
        var model = Model(auth, Kind(kind));
        var program = _engine.Render("app/Program", model);

        model.UseSecurityGuard.Should().BeTrue();
        program.Should().Contain("builder.Services.AddModulusSecurityGuard(builder.Configuration);")
            .And.Contain("using Modulus.AspNetCore.Security.Policy;")
            .And.Contain("using Modulus.Authorization.Extensions;");
        if (model.ExposeApi)
            program.Should().Contain("app.MapOpenApi().Loosen(\"OpenAPI document, mapped in Development only\");");
    }

    [Fact]
    public void Without_an_example_permission_the_fallback_policy_still_comes_from_AddModulusAuthorization()
    {
        var model = Model("keycloak");
        model.ExamplePermission.Should().BeNull();
        model.NeedsFallbackAuthorization.Should().BeTrue();

        var program = _engine.Render("app/Program", model);

        Occurrences(program, "builder.Services.AddModulusAuthorization();").Should().Be(1);
    }

    [Fact]
    public void With_the_example_permission_AddModulusAuthorization_is_registered_once()
    {
        var model = Model("openiddict");
        model.NeedsFallbackAuthorization.Should().BeFalse();

        Occurrences(_engine.Render("app/Program", model), "builder.Services.AddModulusAuthorization();").Should().Be(1);
    }

    [Theory]
    [InlineData("none", "api")]
    [InlineData("keycloak", "webapp")]
    public void Hosts_without_an_enforceable_sign_in_get_no_guard(string auth, string kind)
    {
        var model = Model(auth, Kind(kind));

        model.UseSecurityGuard.Should().BeFalse();
        _engine.Render("app/Program", model).Should().NotContain("AddModulusSecurityGuard").And.NotContain(".Loosen(");
    }

    [Fact]
    public void The_api_allow_list_is_valid_json_listing_the_openapi_document()
    {
        var json = _engine.Render("app/loosening-allowlist.json", Model("openiddict"));

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        doc.RootElement.GetProperty("endpoints").EnumerateArray()
            .Select(e => e.GetProperty("route").GetString())
            .Should().Equal("/openapi/{documentName}.json");
    }

    [Fact]
    public void The_web_host_wires_the_guard_and_lists_its_liveness_probe()
    {
        var model = Model("openiddict", AppKind.WebAppApi);
        model.EnableHealthChecks = true;

        _engine.Render("app/Program.Web", model)
            .Should().Contain("builder.Services.AddModulusSecurityGuard(builder.Configuration);")
            .And.Contain("app.MapHealthChecks(\"/health/live\").Loosen(");

        var json = _engine.Render("app/loosening-allowlist.Web.json", model);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        doc.RootElement.GetProperty("endpoints").EnumerateArray()
            .Select(e => e.GetProperty("route").GetString())
            .Should().Equal("/health/live");
    }

    [Theory]
    [InlineData("openiddict", "api", true)]
    [InlineData("keycloak", "api", true)]
    [InlineData("openiddict", "webapp+api", true)]
    [InlineData("openiddict", "webapp", false)]
    [InlineData("none", "api", false)]
    public void Guarded_API_hosts_ship_the_security_probe_test(string auth, string kind, bool expected)
    {
        var tests = _engine.Render("app/AppTests", Model(auth, Kind(kind)));

        if (expected)
        {
            tests.Should().Contain("public sealed class SecurityProbeTests(ModulusWebAppFactory<ApiEntryPoint> factory)")
                .And.Contain("await SecurityProbeSuite.RunAsync(factory);")
                .And.Contain("using Modulus.Testing.Security;");
        }
        else
        {
            tests.Should().NotContain("SecurityProbeTests");
        }
    }

    private static AppKind Kind(string kind) => kind switch
    {
        "webapp" => AppKind.WebApp,
        "webapp+api" => AppKind.WebAppApi,
        _ => AppKind.Api,
    };

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
