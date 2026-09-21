using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Controllers;
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
