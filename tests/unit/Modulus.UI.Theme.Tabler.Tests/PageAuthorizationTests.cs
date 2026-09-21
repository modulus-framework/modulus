using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Localization;
using Modulus.Storage;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// <c>AddModulusPageAuthorization</c>: a feature UI applies its own permission check only when the app configures one, so a
/// generated web app calls this to keep every page behind a sign-in (the sign-in pages excepted).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PageAuthorizationTests
{
    private static readonly System.Reflection.Assembly[] Parts =
    [
        typeof(Modulus.UI.Files.Pages.Files.IndexModel).Assembly,
        typeof(Modulus.UI.Identity.Pages.Account.LoginModel).Assembly,
    ];

    /// <summary>A cookie scheme that answers 401 to an anonymous caller instead of redirecting (the client would follow the redirect).</summary>
    private static Task<ThemeHost> StartAsync(bool authorize, params string[] anonymousFolders)
        => ThemeHost.StartAsync(
            services: s =>
            {
                s.AddSingleton<IModulusLocalizer, NullLocalizer>();
                s.AddSingleton(Substitute.For<IFileStorage>());
                s.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(o => o.Events.OnRedirectToLogin = c =>
                    {
                        c.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    });
                s.AddAuthorization();
                if (authorize)
                {
                    s.AddModulusPageAuthorization(anonymousFolders);
                }
            },
            applicationParts: Parts,
            pipeline: app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
            });

    private static async Task<HttpStatusCode> StatusAsync(ThemeHost host, string url)
    {
        using var response = await host.Client.GetAsync(url);
        return response.StatusCode;
    }

    [Fact]
    public async Task Without_it_a_feature_page_is_open_to_anyone()
    {
        await using var host = await StartAsync(authorize: false);

        (await StatusAsync(host, "/Files")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_it_an_anonymous_visitor_is_challenged_and_a_signed_in_user_gets_the_page()
    {
        await using var host = await StartAsync(authorize: true);

        (await StatusAsync(host, "/Files")).Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync(host, "/Files?as=alice")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_sign_in_pages_stay_open_by_default()
    {
        await using var host = await StartAsync(authorize: true);

        (await StatusAsync(host, "/Account/AccessDenied")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_app_can_name_the_folders_that_stay_public()
    {
        await using var host = await StartAsync(authorize: true, "/Account", "/Files");

        (await StatusAsync(host, "/Files")).Should().Be(HttpStatusCode.OK);
    }

    private sealed class NullLocalizer : IModulusLocalizer
    {
        public Task<string> GetAsync(string resourceName, string key, CancellationToken ct = default, params object?[] args)
            => Task.FromResult(key);

        public Task<string> GetAsync(System.Globalization.CultureInfo culture, string resourceName, string key, CancellationToken ct = default, params object?[] args)
            => Task.FromResult(key);
    }
}
