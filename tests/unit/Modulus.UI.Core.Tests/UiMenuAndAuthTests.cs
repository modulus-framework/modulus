using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// Spec for the Identity slice: permission-filtered menus, the m-permission
/// tag helper, and the smart cookie-or-bearer scheme.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UiMenuAndAuthTests
{
    private sealed class FakeUser(params string[] permissions) : ICurrentUser
    {
        private readonly HashSet<string> _grants = new(permissions, StringComparer.OrdinalIgnoreCase);
        public Guid? UserId => null;
        public string? UserName => "test";
        public string? Email => null;
        public bool IsAuthenticated => true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => _grants.Contains(permission);
        public IReadOnlyList<string> Permissions => _grants.ToList();
    }

    private sealed class StubModule(params UiMenuItem[] items) : IUiModule
    {
        public ModuleManifest Manifest { get; } = new("t", "T", "1.0.0", [], []);
        public void ConfigureNavigation(UiNavigationBuilder navigation)
        {
            foreach (var item in items)
                navigation.AddItem(item.Id, item.Title, item.Url,
                    icon: item.Icon, requiredPermission: item.RequiredPermission, order: item.Order);
        }
    }

    [Fact]
    public void MenuFilter_HidesDenied_KeepsGrantedAndPublic()
    {
        var menu = new[]
        {
            new UiMenuItem("a", "A", "/a", RequiredPermission: "P.A"),
            new UiMenuItem("b", "B", "/b", RequiredPermission: "P.B"),
            new UiMenuItem("c", "C", "/c"),
        };

        var visible = UiMenuFilter.Apply(menu, p => p == "P.A");

        visible.Select(i => i.Id).Should().BeEquivalentTo("a", "c");
    }

    [Fact]
    public void MenuFilter_DropsPlaceholderGroup_WithNoVisibleChildren()
    {
        var menu = new[]
        {
            new UiMenuItem("g", "G", "#", Children: new[]
            {
                new UiMenuItem("g.kid", "Kid", "/kid", RequiredPermission: "P.Kid"),
            }),
        };

        UiMenuFilter.Apply(menu, _ => false).Should().BeEmpty();
    }

    [Fact]
    public void MenuFilter_KeepsGroupLink_EvenWhenChildrenHidden()
    {
        var menu = new[]
        {
            new UiMenuItem("g", "G", "/g", Children: new[]
            {
                new UiMenuItem("g.kid", "Kid", "/kid", RequiredPermission: "P.Kid"),
            }),
        };

        var visible = UiMenuFilter.Apply(menu, _ => false);

        visible.Should().ContainSingle().Which.Url.Should().Be("/g");
    }

    [Fact]
    public void MenuProvider_FiltersThroughCurrentUser()
    {
        var services = new ServiceCollection();
        services.AddModulusUi();
        services.AddSingleton<IUiModule>(new StubModule(
            new UiMenuItem("a", "A", "/a", RequiredPermission: "P.A"),
            new UiMenuItem("b", "B", "/b")));
        services.AddScoped<ICurrentUser>(_ => new FakeUser("P.A"));
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var menu = scope.ServiceProvider.GetRequiredService<IUiMenuProvider>().GetMenu();

        menu.Select(i => i.Id).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void MenuProvider_DeniesAll_ForNullUser()
    {
        var services = new ServiceCollection();
        services.AddModulusUi();
        services.AddSingleton<IUiModule>(new StubModule(
            new UiMenuItem("a", "A", "/a", RequiredPermission: "P.A")));
        services.AddScoped<ICurrentUser, Modulus.Core.Null.NullCurrentUser>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUiMenuProvider>().GetMenu().Should().BeEmpty();
    }

    private static (TagHelperContext Ctx, TagHelperOutput Out, Func<bool> ChildExecuted) TagHelperIo()
    {
        var ctx = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            Guid.NewGuid().ToString());
        var executed = false;
        var output = new TagHelperOutput(
            "m-permission",
            new TagHelperAttributeList(),
            (_, _) =>
            {
                executed = true;
                return Task.FromResult<TagHelperContent>(new DefaultTagHelperContent());
            });
        return (ctx, output, () => executed);
    }

    [Fact]
    public async Task PermissionTagHelper_Renders_WhenGranted()
    {
        var (ctx, output, childExecuted) = TagHelperIo();
        var helper = new PermissionTagHelper(new FakeUser("P.A")) { Name = "P.A" };

        await helper.ProcessAsync(ctx, output);

        output.TagName.Should().BeNull();
        childExecuted().Should().BeTrue();
    }

    [Fact]
    public async Task PermissionTagHelper_Suppresses_WhenDenied()
    {
        var (ctx, output, childExecuted) = TagHelperIo();
        var helper = new PermissionTagHelper(new FakeUser()) { Name = "P.A" };

        await helper.ProcessAsync(ctx, output);

        output.TagName.Should().BeNull();
        childExecuted().Should().BeFalse();
    }

    [Fact]
    public async Task PermissionTagHelper_Suppresses_WhenNameMissing()
    {
        var (ctx, output, childExecuted) = TagHelperIo();
        var helper = new PermissionTagHelper(new FakeUser("P.A"));

        await helper.ProcessAsync(ctx, output);

        output.TagName.Should().BeNull();
        childExecuted().Should().BeFalse();
    }

    [Fact]
    public void SmartAuth_RegistersSmartScheme_AsDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusSmartAuth();

        var auth = services.BuildServiceProvider()
            .GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        auth.DefaultScheme.Should().Be(ModulusSmartAuthExtensions.DefaultSmartScheme);
        auth.DefaultChallengeScheme.Should().Be(ModulusSmartAuthExtensions.DefaultSmartScheme);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SmartAuth_beats_the_cookie_defaults_AddIdentity_sets_whichever_is_registered_first(bool identityFirst)
    {
        // AddIdentity sets its cookie as the default authenticate, challenge and forbid scheme.
        void AddIdentityDefaults(IServiceCollection s) => s.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = ModulusSmartAuthExtensions.DefaultCookieScheme;
            o.DefaultChallengeScheme = ModulusSmartAuthExtensions.DefaultCookieScheme;
            o.DefaultForbidScheme = ModulusSmartAuthExtensions.DefaultCookieScheme;
            o.DefaultSignInScheme = "Identity.External";
        });

        var services = new ServiceCollection();
        services.AddLogging();
        if (identityFirst) AddIdentityDefaults(services);
        services.AddModulusSmartAuth();
        if (!identityFirst) AddIdentityDefaults(services);

        var auth = services.BuildServiceProvider().GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        auth.DefaultAuthenticateScheme.Should().Be(ModulusSmartAuthExtensions.DefaultSmartScheme, "an explicit authenticate default beats DefaultScheme");
        auth.DefaultChallengeScheme.Should().Be(ModulusSmartAuthExtensions.DefaultSmartScheme);
        auth.DefaultForbidScheme.Should().Be(ModulusSmartAuthExtensions.DefaultSmartScheme);
        auth.DefaultSignInScheme.Should().Be("Identity.External", "sign-in stays Identity's");
    }

    [Theory]
    [InlineData("/api/products", null, "Bearer")]
    [InlineData("/Catalog/Products", "Bearer token", "Bearer")]
    [InlineData("/Catalog/Products", null, "Cookie")]
    [InlineData("/Account/Login", null, "Cookie")]
    public void SmartAuth_SelectsScheme_ByPathAndHeader(string path, string? authHeader, string expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        if (authHeader is not null)
            http.Request.Headers.Authorization = authHeader;

        ModulusSmartAuthExtensions.SelectScheme(http, "Bearer", "Cookie").Should().Be(expected);
    }
}
