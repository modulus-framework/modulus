using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

[Trait("Category", "Unit")]
public sealed class AccountControllerFeatureProviderTests
{
    [Fact]
    public void PopulateFeature_AddsClosedAccountController()
    {
        // Regression: MVC's default ControllerFeatureProvider rejects generic
        // controller types, so without this provider every /account/* route
        // 404s. The closed AccountController<TUser> must be discoverable.
        var provider = new AccountControllerFeatureProvider(typeof(ModulusUser));
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);

        feature.Controllers.Should().Contain(
            typeof(AccountController<ModulusUser>).GetTypeInfo());
    }

    [Fact]
    public void Account_endpoints_are_mapped_under_account_not_the_generic_type_name()
    {
        // Regression: [Route("[controller]")] on a generic controller expands to
        // the CLR name, which mapped every action under "AccountController`1/...".
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().ConfigureApplicationPartManager(m =>
            m.FeatureProviders.Add(new AccountControllerFeatureProvider(typeof(ModulusUser))));
        var app = builder.Build();
        app.MapControllers();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        routes.Should().Contain(["account/forgot-password", "account/reset-password",
            "account/confirm-email", "account/send-confirmation-email", "account/logout"]);
        routes.Should().NotContain(r => r != null && r.Contains('`'));
    }

    [Fact]
    public void PopulateFeature_DoesNotDuplicate_OnRepeatPopulation()
    {
        var provider = new AccountControllerFeatureProvider(typeof(ModulusUser));
        var feature = new ControllerFeature();

        provider.PopulateFeature([], feature);
        provider.PopulateFeature([], feature);

        feature.Controllers
            .Where(t => t.AsType() == typeof(AccountController<ModulusUser>))
            .Should().HaveCount(1);
    }
}
