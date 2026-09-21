using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>Spec for the scoped HX-* response builder (trigger merging, toast/modal/entity events).</summary>
[Trait("Category", "Unit")]
public sealed class HtmxResponseTests
{
    private static (HtmxResponse Htmx, HttpResponse Response) New()
    {
        var http = new DefaultHttpContext();
        return (new HtmxResponse(http.Response), http.Response);
    }

    private static JsonElement Trigger(HttpResponse response)
        => JsonDocument.Parse(response.Headers["HX-Trigger"].ToString()).RootElement;

    [Fact]
    public void Toast_serializes_message_and_type_under_the_canonical_event()
    {
        var (htmx, response) = New();

        htmx.Toast("Saved", "warning");

        var toast = Trigger(response).GetProperty(HtmxResponse.ToastEvent);
        toast.GetProperty("message").GetString().Should().Be("Saved");
        toast.GetProperty("type").GetString().Should().Be("warning");
    }

    [Fact]
    public void Toast_defaults_to_success()
    {
        var (htmx, response) = New();

        htmx.Toast("Saved");

        Trigger(response).GetProperty("modulusToast").GetProperty("type").GetString().Should().Be("success");
    }

    [Fact]
    public void CloseModal_raises_the_modal_close_event()
    {
        var (htmx, response) = New();

        htmx.CloseModal();

        response.Headers["HX-Trigger"].ToString().Should().Be("modulus:modal:close");
    }

    [Fact]
    public void Toast_close_and_entity_events_compose_into_one_header()
    {
        var (htmx, response) = New();

        htmx.Toast("Created").CloseModal().NotifyChanged("catalog.product:changed");

        var root = Trigger(response);
        root.TryGetProperty("modulusToast", out _).Should().BeTrue();
        root.TryGetProperty("modulus:modal:close", out _).Should().BeTrue();
        root.TryGetProperty("catalog.product:changed", out _).Should().BeTrue();
        response.Headers["HX-Trigger"].Should().HaveCount(1);
    }

    [Fact]
    public void Triggering_the_same_event_twice_keeps_the_latest_detail()
    {
        var (htmx, response) = New();

        htmx.Trigger("evt", new { n = 1 }).Trigger("evt", new { n = 2 });

        Trigger(response).GetProperty("evt").GetProperty("n").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData("product:changed")]
    [InlineData("catalog.product:changed")]
    public void NotifyChanged_accepts_entity_change_events(string name)
    {
        var (htmx, response) = New();

        htmx.NotifyChanged(name);

        response.Headers["HX-Trigger"].ToString().Should().Be(name);
    }

    [Theory]
    [InlineData("product")]
    [InlineData("product:updated")]
    [InlineData("changed")]
    public void NotifyChanged_rejects_names_without_the_changed_suffix(string name)
    {
        var (htmx, _) = New();

        var act = () => htmx.NotifyChanged(name);

        act.Should().Throw<ArgumentException>().WithMessage("*:changed*");
    }

    [Fact]
    public void Blank_arguments_are_rejected()
    {
        var (htmx, _) = New();

        ((Action)(() => htmx.Trigger(" "))).Should().Throw<ArgumentException>();
        ((Action)(() => htmx.Toast(""))).Should().Throw<ArgumentException>();
        ((Action)(() => htmx.Redirect(""))).Should().Throw<ArgumentException>();
        ((Action)(() => htmx.Retarget(""))).Should().Throw<ArgumentException>();
        ((Action)(() => htmx.Reswap(""))).Should().Throw<ArgumentException>();
        ((Action)(() => htmx.PushUrl(""))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_accessor_constructor_targets_the_current_response()
    {
        var http = new DefaultHttpContext();
        var htmx = new HtmxResponse(new HttpContextAccessor { HttpContext = http });

        htmx.Redirect("/next");

        http.Response.Headers["HX-Redirect"].ToString().Should().Be("/next");
    }

    [Fact]
    public void Without_an_http_context_the_builder_is_a_harmless_no_op_target()
    {
        var htmx = new HtmxResponse(new HttpContextAccessor());

        var act = () => htmx.Toast("hi").Refresh();

        act.Should().NotThrow();
    }
}
