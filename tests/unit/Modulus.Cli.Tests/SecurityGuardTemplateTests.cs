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

    private static AppModel Model(string auth) => new()
    {
        RootNamespace = "Shop",
        AppName = "Shop",
        Auth = auth,
    };

    [Theory]
    [InlineData("openiddict")]
    [InlineData("keycloak")]
    public void Hosts_with_a_sign_in_wire_the_guard_and_loosen_the_development_OpenAPI_document(string auth)
    {
        var model = Model(auth);
        var program = ApiHostSource.Render(_engine, model);

        model.UseSecurityGuard.Should().BeTrue();
        program.Should().Contain("builder.Services.AddModulusSecurityGuard(builder.Configuration);")
            .And.Contain("builder.Services.AddModulusSecurityAudit(builder.Configuration);")
            .And.Contain("using Modulus.AspNetCore.Security.Policy;")
            .And.Contain("using Modulus.AuditLogging.Security;")
            .And.Contain("using Modulus.Authorization.Extensions;");
        program.Should().Contain("app.MapOpenApi().Loosen(\"OpenAPI document, mapped in Development only\");");
    }

    [Fact]
    public void Without_an_example_module_the_fallback_policy_still_comes_from_AddModulusAuthorization()
    {
        var model = Model("keycloak");
        model.NoExample = true;
        model.ExamplePermission.Should().BeNull();
        model.NeedsFallbackAuthorization.Should().BeTrue();

        var program = ApiHostSource.Render(_engine, model);

        Occurrences(program, "builder.Services.AddModulusAuthorization();").Should().Be(1);
    }

    [Fact]
    public void With_the_example_permission_AddModulusAuthorization_is_registered_once()
    {
        var model = Model("openiddict");
        model.NeedsFallbackAuthorization.Should().BeFalse();

        Occurrences(ApiHostSource.Render(_engine, model), "builder.Services.AddModulusAuthorization();").Should().Be(1);
    }

    [Theory]
    [InlineData("none")]
    public void Hosts_without_an_enforceable_sign_in_get_no_guard(string auth)
    {
        var model = Model(auth);

        model.UseSecurityGuard.Should().BeFalse();
        ApiHostSource.Render(_engine, model).Should().NotContain("AddModulusSecurityGuard").And.NotContain(".Loosen(")
            .And.NotContain("AddModulusSecurityAudit");
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

    [Theory]
    [InlineData("openiddict", true)]
    [InlineData("keycloak", true)]
    [InlineData("none", false)]
    public void Guarded_API_hosts_ship_the_security_probe_test(string auth, bool expected)
    {
        var tests = _engine.Render("app/AppTests", Model(auth));

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

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
