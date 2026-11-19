using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>The Fluid session manager: the Program.cs lines it adds to a host that already has the sign-in endpoints.</summary>
[Trait("Category", "Unit")]
public sealed class UiFluidSessionsTests
{
    private const string Program = """
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddAntiforgery();

        var app = builder.Build();
        var templates = "t";
        app.MapFluidAuthEndpoints(templates);
        app.MapFallback(() => 1);
        """;

    [Fact]
    public void EnsureProgram_registers_the_client_and_maps_the_endpoints_once()
    {
        var once = UiFluidSessions.EnsureProgram(Program, "Shop");
        var twice = UiFluidSessions.EnsureProgram(once, "Shop");

        once.Should().Contain("using Shop.Web.Endpoints;").And.Contain("using Shop.Web.Services.ApiClients;");
        once.Should().Contain("builder.Services.AddSessionsApiClient(builder.Configuration);");
        once.IndexOf("AddSessionsApiClient(", StringComparison.Ordinal)
            .Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        once.Should().Contain("app.MapAccountSessions(templates);");
        twice.Should().Be(once);
    }

    [Fact]
    public void EnsureProgram_refuses_a_host_without_the_sign_in_endpoints()
    {
        var act = () => UiFluidSessions.EnsureProgram(Program.Replace("app.MapFluidAuthEndpoints(templates);", ""), "Shop");

        act.Should().Throw<InvalidOperationException>().WithMessage("*MapFluidAuthEndpoints*");
    }
}
