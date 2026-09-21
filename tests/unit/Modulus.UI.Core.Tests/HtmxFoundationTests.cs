using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// Spec for the HTMX foundation (§5): request detection, scoped trigger
/// merging, PageOrPartial routing, 422 validation mapping, and htmx-aware
/// cookie redirects.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HtmxFoundationTests
{
    private sealed class StubPage : HtmxPageModel
    {
        public IActionResult GoPartial(string view, object? model = null)
            => PageOrPartial(view, model);

        public Task<IActionResult> Run(
            Func<Task> action, string partial, Func<IActionResult> ok)
            => HandleAsync(action, partial, ok);

        public Task<IActionResult> RunWithModel(
            Func<Task> action, string partial, object? model, Func<IActionResult> ok)
            => HandleAsync(action, partial, model, ok);
    }

    private static StubPage Build(bool htmx, bool boosted = false, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = provider };
        if (htmx)
            http.Request.Headers["HX-Request"] = "true";
        if (boosted)
            http.Request.Headers["HX-Boosted"] = "true";

        var page = new StubPage
        {
            PageContext = new PageContext(
                new ActionContext(http, new RouteData(), new PageActionDescriptor())),
        };
        return page;
    }

    [Fact]
    public void IsHtmxFragment_True_OnlyForNonBoosted()
    {
        Build(htmx: true).HttpContext.Request.IsHtmxFragment().Should().BeTrue();
        Build(htmx: true, boosted: true).HttpContext.Request.IsHtmxFragment().Should().BeFalse();
        Build(htmx: false).HttpContext.Request.IsHtmxFragment().Should().BeFalse();
    }

    [Fact]
    public void HtmxTarget_ReturnsHeader_WhenPresent()
    {
        var page = Build(htmx: true);
        page.HttpContext.Request.Headers["HX-Target"] = "product-table";
        page.HttpContext.Request.HtmxTarget().Should().Be("product-table");
        Build(htmx: false).HttpContext.Request.HtmxTarget().Should().BeNull();
    }

    [Fact]
    public void HtmxResponse_MergesTriggers_IntoSingleHeader()
    {
        var http = new DefaultHttpContext();
        var htmx = new HtmxResponse(http.Response);

        htmx.Toast("Created").Trigger("products:changed");

        var raw = http.Response.Headers["HX-Trigger"].ToString();
        raw.Should().Contain("modulusToast").And.Contain("products:changed");
    }

    [Fact]
    public void HtmxResponse_SingleBareTrigger_StaysBare()
    {
        var http = new DefaultHttpContext();
        var htmx = new HtmxResponse(http.Response);

        htmx.Trigger("products:changed");

        http.Response.Headers["HX-Trigger"].ToString().Should().Be("products:changed");
    }

    [Fact]
    public void HtmxResponse_Helpers_SetHeaders()
    {
        var http = new DefaultHttpContext();
        var htmx = new HtmxResponse(http.Response);

        htmx.Redirect("/login").Refresh().Retarget("#main").Reswap("outerHTML").PushUrl("/products");

        http.Response.Headers["HX-Redirect"].ToString().Should().Be("/login");
        http.Response.Headers["HX-Refresh"].ToString().Should().Be("true");
        http.Response.Headers["HX-Retarget"].ToString().Should().Be("#main");
        http.Response.Headers["HX-Reswap"].ToString().Should().Be("outerHTML");
        http.Response.Headers["HX-Push-Url"].ToString().Should().Be("/products");
    }

    [Fact]
    public void PageOrPartial_ReturnsPartial_ForFragment_PreservingModelState()
    {
        var page = Build(htmx: true);
        page.ModelState.AddModelError(string.Empty, "Name: Required.");
        var model = new { Id = 1 };

        var result = page.GoPartial("_Table", model);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_Table");
        partial.Model.Should().Be(model);
        partial.ViewData!.ModelState.IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageOrPartial_ReturnsPage_ForNormalAndBoosted()
    {
        Build(htmx: false).GoPartial("_Table").Should().BeOfType<PageResult>();
        Build(htmx: true, boosted: true).GoPartial("_Table").Should().BeOfType<PageResult>();
    }

    [Fact]
    public async Task HandleAsync_MapsValidation_To422Partial()
    {
        var page = Build(htmx: true);
        Task Fail() => throw new ValidationException(["Name: Required."]);

        var result = await page.Run(Fail, "_Form", () => new NoContentResult());

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_Form");
        page.ModelState.IsValid.Should().BeFalse();
        page.HttpContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task HandleAsync_PassesThrough_OnSuccess()
    {
        var page = Build(htmx: true);
        var called = false;
        Task Ok() { called = true; return Task.CompletedTask; }

        var result = await page.Run(Ok, "_Form", () => new NoContentResult());

        called.Should().BeTrue();
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public void AddModulusUi_RegistersHtmxResponse_AndAntiforgeryHeader()
    {
        var services = new ServiceCollection();
        services.AddModulusUi();

        services.Should().Contain(d =>
            d.ServiceType == typeof(HtmxResponse) &&
            d.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(d =>
            d.ServiceType == typeof(IHttpContextAccessor));

        var provider = services.BuildServiceProvider();
        var antiforgery = provider
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>();
        antiforgery.Value.HeaderName.Should().Be("RequestVerificationToken");
    }

    [Fact]
    public async Task CookieRedirect_SendsHxRedirect_ForHtmx()
    {
        var options = new CookieAuthenticationOptions();
        options.UseModulusHtmxRedirects();

        var http = new DefaultHttpContext();
        http.Request.Headers["HX-Request"] = "true";
        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var ctx = new RedirectContext<CookieAuthenticationOptions>(
            http,
            scheme,
            options,
            new AuthenticationProperties(),
            "/Account/Login?ReturnUrl=%2F");

        await options.Events.OnRedirectToLogin!(ctx);

        http.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        http.Response.Headers["HX-Redirect"].ToString().Should().Be("/Account/Login?ReturnUrl=%2F");
    }
}
