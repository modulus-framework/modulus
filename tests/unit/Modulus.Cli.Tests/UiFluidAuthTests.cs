using FluentAssertions;
using Modulus.Cli.Services;
using Xunit;

namespace Modulus.Cli.Tests;

[Trait("Category", "Unit")]
public sealed class UiFluidAuthTests
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
        var once = UiFluidAuth.EnsureProgram(Program, "Shop");
        var twice = UiFluidAuth.EnsureProgram(once, "Shop");

        once.Should().Contain("using Shop.Web.Services.ApiClients;");
        once.Should().Contain("AddModulusHttpClient<AccountApiClient>");
        once.IndexOf("AddModulusHttpClient<AccountApiClient>", StringComparison.Ordinal)
            .Should().BeLessThan(once.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        once.Should().Contain("app.MapAccountRecovery(templates);");
        twice.Should().Be(once);
    }

    [Fact]
    public void EnsureProgram_refuses_a_host_without_the_sign_in_endpoints()
    {
        var act = () => UiFluidAuth.EnsureProgram(Program.Replace("app.MapFluidAuthEndpoints(templates);", ""), "Shop");

        act.Should().Throw<InvalidOperationException>().WithMessage("*MapFluidAuthEndpoints*");
    }
}

[Trait("Category", "Unit")]
public sealed class UiFluidComponentCatalogTests
{
    [Fact]
    public void Every_fluid_component_is_in_the_catalog()
    {
        UiComponents.FluidKeys.Should().OnlyContain(k => UiComponents.All.Any(c => c.Key == k));
    }

    [Fact]
    public void Fluid_offers_only_the_liquid_components()
    {
        var fluid = UiComponents.All.Where(c => c.Supports("fluid")).Select(c => c.Key);

        fluid.Should().BeEquivalentTo(UiComponents.FluidKeys);
        UiComponents.All.First(c => c.Key == "select").SupportedEngines.Should().NotContain("fluid").And.Contain("mvc");
    }
}
