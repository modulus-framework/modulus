using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.Localization;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// H21: the five Account pages used to hardcode English directly in the
/// <c>.cshtml</c> instead of going through <c>IdentityUiLocalization</c>'s
/// ~32 declared keys — <c>LoginModel</c>/<c>RegisterModel</c>'s <c>TextAsync</c>
/// helper existed but was <c>private</c>, so the view could not call it, and
/// <c>SignOutModel</c>/the code-behind-less <c>LoggedOut</c>/<c>AccessDenied</c>
/// pages had no localizer access at all. These tests render the real Razor
/// pipeline with the same key-echoing <c>KeyLocalizer</c> double used
/// throughout <see cref="FeatureUiRenderTests"/>, so a rendered key string
/// (e.g. "Login.Title") proves the view asked the localizer for it instead of
/// emitting a hardcoded literal.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityAccountRenderTests
{
    private sealed class KeyLocalizer : IModulusLocalizer
    {
        public Task<string> GetAsync(string resourceName, string key, CancellationToken ct = default, params object?[] args)
            => Task.FromResult(key);

        public Task<string> GetAsync(System.Globalization.CultureInfo culture, string resourceName, string key, CancellationToken ct = default, params object?[] args)
            => Task.FromResult(key);
    }

    private static readonly System.Reflection.Assembly[] Parts =
    [
        typeof(Modulus.UI.Identity.Pages.Account.LoginModel).Assembly,
    ];

    private static UserManager<ModulusUser> FakeUserManager()
    {
        var store = Substitute.For<IUserStore<ModulusUser>>();
        return Substitute.For<UserManager<ModulusUser>>(
            store,
            Options.Create(new IdentityOptions()),
            null!, null!, null!, null!, null!, null!, null!);
    }

    private static SignInManager<ModulusUser> FakeSignInManager(UserManager<ModulusUser> users)
        => Substitute.For<SignInManager<ModulusUser>>(
            users,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<ModulusUser>>(),
            Options.Create(new IdentityOptions()),
            Substitute.For<ILogger<SignInManager<ModulusUser>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<ModulusUser>>());

    private static Task<ThemeHost> AccountHost()
        => ThemeHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, KeyLocalizer>();
                var users = FakeUserManager();
                s.AddSingleton(users);
                s.AddSingleton(FakeSignInManager(users));
                // RegisterModel.OnGet 404s unless self-registration is on.
                s.Configure<Modulus.UI.Identity.IdentityUiOptions>(o => o.AllowSelfRegistration = true);
            },
            applicationParts: Parts);

    [Fact]
    public async Task Login_page_asks_the_localizer_for_every_label_instead_of_hardcoding_English()
    {
        await using var host = await AccountHost();

        var html = await host.GetStringAsync("/Account/Login");

        html.Should().Contain("Login.Title");
        html.Should().Contain("Login.Email");
        html.Should().Contain("Login.Password");
        html.Should().Contain("Login.RememberMe");
        html.Should().Contain("Login.Submit");
    }

    [Fact]
    public async Task Register_page_asks_the_localizer_for_every_label_instead_of_hardcoding_English()
    {
        await using var host = await AccountHost();

        var html = await host.GetStringAsync("/Account/Register");

        html.Should().Contain("Register.Title");
        html.Should().Contain("Register.Email");
        html.Should().Contain("Register.Password");
        html.Should().Contain("Register.ConfirmPassword");
        html.Should().Contain("Register.Submit");
    }

    [Fact]
    public async Task AccessDenied_page_asks_the_localizer_instead_of_hardcoding_English()
    {
        await using var host = await AccountHost();

        var html = await host.GetStringAsync("/Account/AccessDenied");

        html.Should().Contain("AccessDenied.Title");
        html.Should().Contain("AccessDenied.Message");
    }

    [Fact]
    public async Task LoggedOut_page_asks_the_localizer_instead_of_hardcoding_English()
    {
        await using var host = await AccountHost();

        var html = await host.GetStringAsync("/Account/LoggedOut");

        html.Should().Contain("LoggedOut.Title");
        html.Should().Contain("LoggedOut.Message");
        html.Should().Contain("LoggedOut.LoginLink");
    }
}
