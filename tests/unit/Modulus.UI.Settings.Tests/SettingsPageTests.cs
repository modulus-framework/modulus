using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Settings;
using Modulus.UI.Settings.Pages.Settings;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Settings.Tests;

/// <summary>
/// Spec for the setting browser (visible-only, grouped, effective values)
/// and editor (scoped write, blank-removes, scope preconditions).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingsPageTests
{
    // NullCurrentTenant.IsHost is true, so these existing (non-tenant-focused)
    // tests keep write access to every scope, including Global; the
    // host-only Global check is exercised separately below with an explicit
    // non-host tenant.
    private static (EditModel Model, ISettingManager Manager) BuildEdit(
        SettingDefinition? definition = null, ICurrentTenant? currentTenant = null)
    {
        var registry = Substitute.For<ISettingDefinitionRegistry>();
        registry.Find(Arg.Any<string>()).Returns(definition);
        var manager = Substitute.For<ISettingManager>();
        var user = Substitute.For<ICurrentUser>();
        var model = new EditModel(
            registry, manager, currentTenant ?? new NullCurrentTenant(), user, new TestLocalizer());
        return (model, manager);
    }

    [Fact]
    public async Task Index_ListsVisibleOnly_GroupedWithEffectiveValues()
    {
        var registry = Substitute.For<ISettingDefinitionRegistry>();
        registry.List().Returns(
        [
            new SettingDefinition("App.Theme", "light", "Theme", null),
            new SettingDefinition("App.Name", "Acme", "Name", null),
            new SettingDefinition("Internal.Token", null, null, null, IsVisibleToClients: false),
        ]);
        var manager = Substitute.For<ISettingManager>();
        manager.GetOrNullAsync("App.Theme", Arg.Any<CancellationToken>()).Returns("dark");
        manager.GetOrNullAsync("App.Name", Arg.Any<CancellationToken>()).Returns((string?)null);
        var model = new IndexModel(registry, manager, new TestLocalizer());

        await model.OnGetAsync(default);

        model.Groups.Should().ContainSingle().Which.Group.Should().Be("App");
        var rows = model.Groups[0].Rows;
        rows.Should().HaveCount(2);
        rows.Single(r => r.Definition.Name == "App.Theme").EffectiveValue.Should().Be("dark");
    }

    [Fact]
    public async Task Edit_Get_UnknownName_ReturnsNotFound()
    {
        var (model, _) = BuildEdit(definition: null);

        (await model.OnGetAsync("nope", default)).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Edit_Get_KnownName_PrefillsEffectiveValue()
    {
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"));
        manager.GetOrNullAsync("App.Theme", Arg.Any<CancellationToken>()).Returns("dark");

        var result = await model.OnGetAsync("App.Theme", default);

        result.Should().BeOfType<PageResult>();
        model.Input.Value.Should().Be("dark");
    }

    [Fact]
    public async Task Edit_Post_Value_SetsScopedValue_RedirectsToIndex()
    {
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"));
        model.Input = new EditModel.InputModel { Value = "dark", Scope = "Global" };

        var result = await model.OnPostAsync("App.Theme", default);

        result.Should().BeOfType<RedirectToPageResult>();
        await manager.Received(1).SetAsync(
            "App.Theme", "dark", SettingScope.Global, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_Post_BlankValue_RemovesOverride()
    {
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"));
        model.Input = new EditModel.InputModel { Value = "  ", Scope = "Global" };

        await model.OnPostAsync("App.Theme", default);

        await manager.Received(1).RemoveAsync(
            "App.Theme", SettingScope.Global, Arg.Any<CancellationToken>());
        await manager.DidNotReceiveWithAnyArgs().SetAsync(
            default!, default!, default, default);
    }

    [Fact]
    public async Task Edit_Post_TenantScope_WithoutTenant_RendersError()
    {
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"));
        model.Input = new EditModel.InputModel { Value = "dark", Scope = "Tenant" };

        var result = await model.OnPostAsync("App.Theme", default);

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        await manager.DidNotReceiveWithAnyArgs().SetAsync(
            default!, default!, default, default);
    }

    [Fact]
    public async Task Edit_Post_GlobalScope_FromNonHostTenant_RendersError()
    {
        // Global is "shared by the whole installation" — a tenant-scoped
        // settings:manage holder must not be able to write it and change
        // behavior for every other tenant.
        var tenant = new FakeCurrentTenant(Guid.NewGuid(), isHost: false);
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"), tenant);
        model.Input = new EditModel.InputModel { Value = "dark", Scope = "Global" };

        var result = await model.OnPostAsync("App.Theme", default);

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        await manager.DidNotReceiveWithAnyArgs().SetAsync(
            default!, default!, default, default);
    }

    [Fact]
    public async Task Edit_Post_GlobalScope_FromHost_Succeeds()
    {
        var host = new FakeCurrentTenant(tenantId: null, isHost: true);
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"), host);
        model.Input = new EditModel.InputModel { Value = "dark", Scope = "Global" };

        var result = await model.OnPostAsync("App.Theme", default);

        result.Should().BeOfType<RedirectToPageResult>();
        await manager.Received(1).SetAsync(
            "App.Theme", "dark", SettingScope.Global, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_Post_UnknownScope_RendersError()
    {
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"));
        model.Input = new EditModel.InputModel { Value = "dark", Scope = "Planet" };

        var result = await model.OnPostAsync("App.Theme", default);

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        await manager.DidNotReceiveWithAnyArgs().SetAsync(
            default!, default!, default, default);
    }

    private static void AsHtmx(EditModel model)
    {
        model.PageContext = new PageContext(
            new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new PageActionDescriptor()));
        model.Request.Headers["HX-Request"] = "true";
    }

    private static string HxTrigger(EditModel model)
        => model.Response.Headers["HX-Trigger"].ToString();

    [Fact]
    public async Task Edit_Post_Htmx_Value_ReturnsForm_WithToast()
    {
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"));
        model.Input = new EditModel.InputModel { Value = "dark", Scope = "Global" };
        manager.GetOrNullAsync("App.Theme", Arg.Any<CancellationToken>()).Returns("dark");
        AsHtmx(model);

        var result = await model.OnPostAsync("App.Theme", default);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_EditForm");
        partial.Model.Should().Be(model);
        model.Input.Value.Should().Be("dark");
        HxTrigger(model).Should().Contain("modulusToast");
        await manager.Received(1).SetAsync(
            "App.Theme", "dark", SettingScope.Global, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_Post_Htmx_BlankValue_RemovesOverride_ReturnsForm()
    {
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"));
        model.Input = new EditModel.InputModel { Value = "  ", Scope = "Global" };
        AsHtmx(model);

        var result = await model.OnPostAsync("App.Theme", default);

        result.Should().BeOfType<PartialViewResult>()
            .Which.ViewName.Should().Be("_EditForm");
        await manager.Received(1).RemoveAsync(
            "App.Theme", SettingScope.Global, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_Post_Htmx_UnknownScope_ReturnsForm_WithErrors()
    {
        var (model, manager) = BuildEdit(new SettingDefinition("App.Theme", "light"));
        model.Input = new EditModel.InputModel { Value = "dark", Scope = "Planet" };
        AsHtmx(model);

        var result = await model.OnPostAsync("App.Theme", default);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_EditForm");
        partial.ViewData!.ModelState.IsValid.Should().BeFalse();
        await manager.DidNotReceiveWithAnyArgs().SetAsync(
            default!, default!, default, default);
    }

    private sealed class FakeCurrentTenant(Guid? tenantId, bool isHost) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;
        public string? TenantSlug => null;
        public bool IsAvailable => tenantId is not null;
        public bool IsHost => isHost;
        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
