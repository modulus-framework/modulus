using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.UI.Identity.Pages.Account;
using NSubstitute;
using Xunit;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Modulus.UI.Identity.Tests;

/// <summary>
/// Spec for the cookie login form: credential checks mirror the password
/// grant (inactive rejected, lock-out honoured), failures stay non-committal,
/// and only local return urls are honored (open-redirect protection).
/// </summary>
[Trait("Category", "Unit")]
public sealed class LoginModelTests
{
    private static (LoginModel Model, UserManager<ModulusUser> Users, SignInManager<ModulusUser> SignIns) Build()
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
            Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<ModulusUser>>());

        var model = new LoginModel(signIns, users, new FakeLocalizer());
        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(
            new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()));
        model.Url = new UrlHelper(model.PageContext);
        return (model, users, signIns);
    }

    private static ModulusUser ActiveUser(string email = "a@b.c")
        => new() { UserName = email, Email = email, IsActive = true };

    [Fact]
    public async Task Post_ValidCredentials_RedirectsToLocalReturnUrl()
    {
        var (model, users, signIns) = Build();
        var user = ActiveUser();
        users.FindByEmailAsync("a@b.c").Returns(user);
        signIns.CanSignInAsync(user).Returns(true);
        signIns.PasswordSignInAsync(user, "pw", false, true).Returns(IdentitySignInResult.Success);
        model.Input = new LoginModel.InputModel { Email = "a@b.c", Password = "pw" };

        var result = await model.OnPostAsync("/orders");

        result.Should().BeOfType<LocalRedirectResult>().Which.Url.Should().Be("/orders");
    }

    [Fact]
    public async Task Post_ExternalReturnUrl_RedirectsHome()
    {
        var (model, users, signIns) = Build();
        var user = ActiveUser();
        users.FindByEmailAsync("a@b.c").Returns(user);
        signIns.CanSignInAsync(user).Returns(true);
        signIns.PasswordSignInAsync(user, "pw", false, true).Returns(IdentitySignInResult.Success);
        model.Input = new LoginModel.InputModel { Email = "a@b.c", Password = "pw" };

        var result = await model.OnPostAsync("https://evil.example/phish");

        result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("~/");
    }

    [Fact]
    public async Task Post_UnknownUser_RendersSameError_WithoutPasswordCheck()
    {
        var (model, users, signIns) = Build();
        users.FindByEmailAsync(Arg.Any<string>()).Returns((ModulusUser?)null);
        model.Input = new LoginModel.InputModel { Email = "ghost@b.c", Password = "pw" };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        model.ModelState[string.Empty]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("[Login.InvalidAttempt]");
        await signIns.DidNotReceive().PasswordSignInAsync(Arg.Any<ModulusUser>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Post_InactiveUser_RendersSameError_WithoutPasswordCheck()
    {
        var (model, users, signIns) = Build();
        var user = ActiveUser();
        user.IsActive = false;
        users.FindByEmailAsync("a@b.c").Returns(user);
        model.Input = new LoginModel.InputModel { Email = "a@b.c", Password = "pw" };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ModelState[string.Empty]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("[Login.InvalidAttempt]");
        await signIns.DidNotReceive().PasswordSignInAsync(Arg.Any<ModulusUser>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Post_LockedOut_RendersLockedOutMessage()
    {
        var (model, users, signIns) = Build();
        var user = ActiveUser();
        users.FindByEmailAsync("a@b.c").Returns(user);
        signIns.CanSignInAsync(user).Returns(true);
        signIns.PasswordSignInAsync(user, "pw", false, true).Returns(IdentitySignInResult.LockedOut);
        model.Input = new LoginModel.InputModel { Email = "a@b.c", Password = "pw" };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ModelState[string.Empty]!.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("[Login.LockedOut]");
    }

    [Fact]
    public async Task Post_InvalidModelState_SkipsCredentialChecks()
    {
        var (model, users, signIns) = Build();
        model.ModelState.AddModelError("Input.Email", "Required");

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        await users.DidNotReceiveWithAnyArgs().FindByEmailAsync(null!);
        await signIns.DidNotReceive().PasswordSignInAsync(Arg.Any<ModulusUser>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
    }
}
