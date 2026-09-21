using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// Spec for the shared HTMX page-model helpers: fragment results carry the
/// view name, model, and a copy of ambient ModelState; toasts and triggers
/// ride the HX-Trigger header; empty results and client redirects behave.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HtmxPageModelTests
{
    private sealed class StubPage : HtmxPageModel
    {
        public bool IsHtmx() => IsHtmxRequest;

        public PartialViewResult Fragment(string view, object? model)
            => HtmxPartial(view, model);

        public ContentResult Empty() => HtmxEmpty();

        public void Toast(string message) => HtmxToast(message);

        public void Trigger(string name) => HtmxTrigger(name);

        public ContentResult Go(string url) => HtmxRedirect(url);
    }

    private static StubPage Build(bool htmx)
    {
        var page = new StubPage
        {
            PageContext = new PageContext(
                new ActionContext(
                    new DefaultHttpContext(),
                    new RouteData(),
                    new PageActionDescriptor())),
        };
        if (htmx)
            page.Request.Headers["HX-Request"] = "true";
        return page;
    }

    [Fact]
    public void IsHtmxRequest_False_WithoutContextOrHeader()
    {
        new StubPage().IsHtmx().Should().BeFalse();
        Build(htmx: false).IsHtmx().Should().BeFalse();
    }

    [Fact]
    public void IsHtmxRequest_True_WithHeader()
    {
        Build(htmx: true).IsHtmx().Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, false)] // plain navigation → shell
    [InlineData(true, false, true)]   // htmx fragment → layout-less
    [InlineData(true, true, false)]   // boosted navigation → shell
    public void IsHtmxFragment_MatchesLayoutSwitch(bool hxRequest, bool boosted, bool expected)
    {
        var page = Build(htmx: hxRequest);
        if (boosted)
            page.Request.Headers["HX-Boosted"] = "true";

        page.IsHtmxFragment.Should().Be(expected);
    }

    [Fact]
    public void IsHtmxFragment_False_WithoutContext()
    {
        new StubPage().IsHtmxFragment.Should().BeFalse();
    }

    [Fact]
    public void HtmxPartial_CarriesViewNameModelAndModelState()
    {
        var page = Build(htmx: true);
        page.ModelState.AddModelError("Name", "Required.");
        var model = new { Id = 1 };

        var result = page.Fragment("_Row", model);

        result.ViewName.Should().Be("_Row");
        result.Model.Should().Be(model);
        result.ViewData!.ModelState.IsValid.Should().BeFalse();
    }

    [Fact]
    public void HtmxEmpty_ReturnsEmptyContent()
    {
        Build(htmx: true).Empty().Content.Should().BeEmpty();
    }

    [Fact]
    public void HtmxToast_SendsModulusToastTrigger()
    {
        var page = Build(htmx: true);

        page.Toast("Saved.");

        var trigger = page.Response.Headers["HX-Trigger"].ToString();
        trigger.Should().Contain("modulusToast").And.Contain("Saved.");
    }

    [Fact]
    public void HtmxTrigger_WithoutDetail_SendsBareName()
    {
        var page = Build(htmx: true);

        page.Trigger("refresh-list");

        page.Response.Headers["HX-Trigger"].ToString().Should().Be("refresh-list");
    }

    [Fact]
    public void HtmxRedirect_SendsRedirectHeader_WithEmptyBody()
    {
        var page = Build(htmx: true);

        var result = page.Go("/users");

        result.Content.Should().BeEmpty();
        page.Response.Headers["HX-Redirect"].ToString().Should().Be("/users");
    }

    [Fact]
    public void UiAlert_Factories_MapToTablerColors()
    {
        UiAlert.Success("ok").Should().Be(new UiAlert("success", "ok"));
        UiAlert.Info("note").Type.Should().Be("info");
        UiAlert.Warning("careful").Type.Should().Be("warning");
        UiAlert.Danger("boom").Type.Should().Be("danger");
    }

    [Theory]
    [InlineData("bell")]
    [InlineData("building")]
    [InlineData("clipboard-list")]
    [InlineData("folder")]
    [InlineData("key")]
    [InlineData("lock")]
    [InlineData("settings")]
    [InlineData("shield-lock")]
    [InlineData("users")]
    public void UiIcons_KnownNames_RenderPathMarkup(string name)
    {
        UiIcons.Known.Should().Contain(name);
        UiIcons.GetInnerSvg(name).Should().Contain("<path");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("no-such-icon")]
    public void UiIcons_UnknownNames_RenderNothing(string? name)
    {
        UiIcons.GetInnerSvg(name).Should().BeEmpty();
    }
}
