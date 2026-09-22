using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.MultiTenancy;
using Modulus.UI.Identity.Pages.Account;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Identity.Tests;

/// <summary>
/// Spec for self-registration: 404 when disabled, tenant-stamped users,
/// confirmed-email hand-off, and sign-out navigation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RegisterModelTests
{
    private static (RegisterModel Model, UserManager<ModulusUser> Users, SignInManager<ModulusUser> SignIns, CurrentTenant Tenant) Build(
        bool allowSelfRegistration,
        bool requireConfirmedEmail = false)
    {
        var store = Substitute.For<IUserStore<ModulusUser>>();
        var users = Substitute.For<UserManager<ModulusUser>>(
            store,
            Options.Create(new IdentityOptions()),
            null!, null!, null!, null!, null!, null!, null!);
        var signIns = Substitute.For<SignInManager<ModulusUser>>(
            users,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<ModulusUser>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<ModulusUser>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<ModulusUser>>());

        var tenant = new CurrentTenant();
        var model = new RegisterModel(
            users,
            signIns,
            tenant,
            Options.Create(new IdentityUiOptions { AllowSelfRegistration = allowSelfRegistration }),
            Options.Create(new ModulusIdentityOptions { RequireConfirmedEmail = requireConfirmedEmail }),
            new FakeLocalizer());
        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(
            new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()));
        model.Url = new UrlHelper(model.PageContext);
        model.TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());
        return (model, users, signIns, tenant);
    }

    [Fact]
    public void Get_WhenDisabled_ReturnsNotFound()
    {
        var (model, _, _, _) = Build(allowSelfRegistration: false);

        model.OnGet().Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Post_WhenDisabled_ReturnsNotFound()
    {
        var (model, _, _, _) = Build(allowSelfRegistration: false);

        (await model.OnPostAsync()).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Post_PasswordMismatch_RendersError()
    {
        var (model, _, _, _) = Build(allowSelfRegistration: true);
        model.Input = new RegisterModel.InputModel
        {
            Email = "a@b.c",
            Password = "pw-one",
            ConfirmPassword = "pw-two",
        };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Post_Success_CreatesTenantUser_SignsIn_RedirectsHome()
    {
        var (model, users, signIns, tenant) = Build(allowSelfRegistration: true);
        var tenantId = Guid.NewGuid();
        ModulusUser? created = null;
        users.CreateAsync(Arg.Do<ModulusUser>(u => created = u), "pw-123456")
            .Returns(IdentityResult.Success);
        model.Input = new RegisterModel.InputModel
        {
            Email = "a@b.c",
            Password = "pw-123456",
            ConfirmPassword = "pw-123456",
        };

        IActionResult result;
        using (tenant.Change(new TenantInfo(tenantId, "acme")))
            result = await model.OnPostAsync();

        created.Should().NotBeNull();
        created!.Email.Should().Be("a@b.c");
        created.TenantId.Should().Be(tenantId);
        await signIns.Received(1).SignInAsync(created, false, null);
        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("~/");
    }

    [Fact]
    public async Task Post_WhenEmailConfirmationRequired_RedirectsToLogin()
    {
        var (model, users, _, _) = Build(allowSelfRegistration: true, requireConfirmedEmail: true);
        users.CreateAsync(Arg.Any<ModulusUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        model.Input = new RegisterModel.InputModel
        {
            Email = "a@b.c",
            Password = "pw-123456",
            ConfirmPassword = "pw-123456",
        };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>().Which.PageName.Should().Be("./Login");
    }
}

/// <summary>Sign-out posts to the confirmation page, signs out, lands on LoggedOut.</summary>
[Trait("Category", "Unit")]
public sealed class SignOutModelTests
{
    [Fact]
    public async Task Post_SignsOut_RedirectsToLoggedOut()
    {
        var store = Substitute.For<IUserStore<ModulusUser>>();
        var users = Substitute.For<UserManager<ModulusUser>>(
            store,
            Options.Create(new IdentityOptions()),
            null!, null!, null!, null!, null!, null!, null!);
        var signIns = Substitute.For<SignInManager<ModulusUser>>(
            users,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<ModulusUser>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<ModulusUser>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<ModulusUser>>());
        var model = new SignOutModel(signIns, new FakeLocalizer());

        var result = await model.OnPostAsync();

        await signIns.Received(1).SignOutAsync();
        result.Should().BeOfType<RedirectToPageResult>().Which.PageName.Should().Be("./LoggedOut");
    }
}
