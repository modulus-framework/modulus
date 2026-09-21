using FluentAssertions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.UI;
using Modulus.UI.Theming;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for named layout slots: ordering, permission filtering, empty-slot handling.</summary>
[Trait("Category", "Unit")]
public sealed class SlotRendererTests
{
    private sealed class Contribution(string slot, string html, int order = 0, string? permission = null) : ISlotContributor
    {
        public string Slot => slot;

        public int Order => order;

        public string? RequiredPermission => permission;

        public ValueTask<IHtmlContent?> RenderAsync(SlotContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IHtmlContent?>(html.Length == 0 ? null : new HtmlString(html));
    }

    private static SlotRenderer Renderer(bool granted, params ISlotContributor[] contributors)
    {
        var user = Substitute.For<ICurrentUser>();
        user.HasPermission(Arg.Any<string>()).Returns(granted);
        return new SlotRenderer(contributors, user);
    }

    private static ViewContext Context() => new() { HttpContext = new DefaultHttpContext() };

    private static string Text(IHtmlContent? content)
    {
        using var writer = new StringWriter();
        content?.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact]
    public async Task Contributions_render_in_ascending_order()
    {
        var renderer = Renderer(true,
            new Contribution(UiSlots.TopbarEnd, "<b>2</b>", order: 20),
            new Contribution(UiSlots.TopbarEnd, "<b>1</b>", order: 10));

        Text(await renderer.RenderAsync(UiSlots.TopbarEnd, Context())).Should().Be("<b>1</b><b>2</b>");
    }

    [Fact]
    public async Task Only_the_requested_slot_is_rendered_and_matching_ignores_case()
    {
        var renderer = Renderer(true,
            new Contribution("topbar.end", "<i>end</i>"),
            new Contribution(UiSlots.Footer, "<i>footer</i>"));

        Text(await renderer.RenderAsync(UiSlots.TopbarEnd, Context())).Should().Be("<i>end</i>");
    }

    [Fact]
    public async Task An_empty_slot_yields_null_so_layouts_can_omit_wrappers()
    {
        var renderer = Renderer(true, new Contribution(UiSlots.Footer, "<i>f</i>"));

        (await renderer.RenderAsync(UiSlots.SidebarTop, Context())).Should().BeNull();
    }

    [Fact]
    public async Task Contributors_returning_null_render_nothing_and_do_not_count_as_content()
    {
        var renderer = Renderer(true, new Contribution(UiSlots.Footer, string.Empty));

        // The slot has a contributor, so a (possibly empty) builder comes back, but no markup.
        Text(await renderer.RenderAsync(UiSlots.Footer, Context())).Should().BeEmpty();
    }

    [Fact]
    public async Task Permission_gated_contributions_are_dropped_for_users_without_the_permission()
    {
        var gated = new Contribution(UiSlots.TopbarEnd, "<i>secret</i>", permission: "admin");
        var open = new Contribution(UiSlots.TopbarEnd, "<i>open</i>", order: 1);

        Text(await Renderer(false, gated, open).RenderAsync(UiSlots.TopbarEnd, Context())).Should().Be("<i>open</i>");
        Text(await Renderer(true, gated, open).RenderAsync(UiSlots.TopbarEnd, Context())).Should().Be("<i>secret</i><i>open</i>");
    }

    [Fact]
    public async Task Fully_gated_slots_yield_null()
    {
        var renderer = Renderer(false, new Contribution(UiSlots.TopbarEnd, "<i>secret</i>", permission: "admin"));

        (await renderer.RenderAsync(UiSlots.TopbarEnd, Context())).Should().BeNull();
    }

    [Fact]
    public async Task Blank_slot_names_are_rejected()
    {
        var renderer = Renderer(true);

        var act = async () => await renderer.RenderAsync(" ", Context());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void SlotContext_exposes_request_services()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var view = new ViewContext { HttpContext = new DefaultHttpContext { RequestServices = provider } };

        new SlotContext(view).Services.Should().BeSameAs(provider);
    }

    [Fact]
    public void AddSlotContributor_is_idempotent_per_type()
    {
        var services = new ServiceCollection();

        services.AddSlotContributor<TypedContributor>();
        services.AddSlotContributor<TypedContributor>();

        services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetServices<ISlotContributor>().Should().ContainSingle();
    }

    private sealed class TypedContributor : ISlotContributor
    {
        public string Slot => UiSlots.Footer;

        public ValueTask<IHtmlContent?> RenderAsync(SlotContext context, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IHtmlContent?>(null);
    }
}
