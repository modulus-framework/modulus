using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Authorization;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Null;
using Modulus.MultiTenancy;
using Modulus.Navigation;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The navigation engine (phase 6): the menu a person sees, the company overrides that narrow it, and the checks a menu passes before it
/// starts. Permissions are answered by a claim in these tests, so the rules under test are the menu's own.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NavigationTests
{
    private static readonly NavigationItem Catalog = new("catalog", "Catalog") { Order = 2 };
    private static readonly NavigationItem Products = new("catalog.products", "Products") { ParentKey = "catalog", Url = "/catalog/products", Permission = "catalog:products:read", Order = 1 };
    private static readonly NavigationItem Orders = new("catalog.orders", "Orders") { ParentKey = "catalog", Url = "/catalog/orders", Permission = "catalog:orders:read" };
    private static readonly NavigationItem Admin = new("admin", "Administration") { Order = 9, Permission = "admin:menu" };
    private static readonly NavigationItem Users = new("admin.users", "Users") { ParentKey = "admin", Url = "/admin/users", Permission = "admin:users:manage" };
    private static readonly NavigationItem Home = new("home", "Home") { Url = "/", Order = 0 };

    private static ServiceProvider Build(params NavigationItem[] items)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, ClaimPermissionHandler>();
        services.AddModulusNavigation(navigation =>
        {
            foreach (var item in items)
                navigation.Add(item);
        });
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Person(params string[] permissions)
        => new(new ClaimsIdentity(permissions.Select(p => new Claim("perm", p)), "test"));

    private static (ServiceProvider Services, CurrentTenant Tenant) BuildWithTenant(params NavigationItem[] items)
    {
        var services = Build(items);
        return (services, (CurrentTenant)services.GetRequiredService<ICurrentTenant>());
    }

    private static IEnumerable<string> Keys(IEnumerable<NavigationNode> nodes)
        => nodes.SelectMany(n => new[] { n.Key }.Concat(Keys(n.Children)));

    [Fact]
    public async Task Menu_ShowsOnlyWhatThePersonMayReach_InOrder()
    {
        using var provider = Build(Home, Catalog, Products, Orders, Admin, Users);
        var navigation = provider.GetRequiredService<INavigationService>();

        var menu = await navigation.GetMenuAsync(Person("catalog:products:read"));

        menu.Select(n => n.Key).Should().Equal("home", "catalog");
        menu[1].Children.Select(n => n.Key).Should().Equal("catalog.products");
        menu[1].Children[0].Url.Should().Be("/catalog/products");
    }

    [Fact]
    public async Task Menu_HidesAGroup_WhenNoChildIsVisible()
    {
        using var provider = Build(Catalog, Products, Orders);
        var navigation = provider.GetRequiredService<INavigationService>();

        (await navigation.GetMenuAsync(Person())).Should().BeEmpty("the catalog group has no visible child");
    }

    [Fact]
    public async Task Menu_ShowsAGroupWithItsOwnLink_EvenWithNoVisibleChild()
    {
        var group = new NavigationItem("reports", "Reports") { Url = "/reports" };
        using var provider = Build(group);

        (await provider.GetRequiredService<INavigationService>().GetMenuAsync(Person())).Select(n => n.Key).Should().Equal("reports");
    }

    [Fact]
    public async Task Override_Hidden_HidesTheItemAndEverythingUnderIt()
    {
        var (provider, tenant) = BuildWithTenant(Home, Catalog, Products, Orders);
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<INavigationService>();
        var person = Person("catalog:products:read", "catalog:orders:read");

        using (tenant.Change(new TenantInfo(Guid.NewGuid(), "acme")))
        {
            await navigation.SetOverrideAsync("catalog", new NavigationOverride(Hidden: true));
            Keys(await navigation.GetMenuAsync(person)).Should().Equal("home");
        }
    }

    [Fact]
    public async Task Override_CannotRevealAnItemThePermissionHides()
    {
        var (provider, tenant) = BuildWithTenant(Home, Admin, Users);
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<INavigationService>();

        using (tenant.Change(new TenantInfo(Guid.NewGuid(), "acme")))
        {
            await navigation.SetOverrideAsync("admin", new NavigationOverride(Hidden: false, Title: "Admin"));
            Keys(await navigation.GetMenuAsync(Person())).Should().Equal(new[] { "home" }, because: "a visible override does not grant the permission");
        }
    }

    [Fact]
    public async Task Override_RetitlesAndReorders_ForTheCompanyOnly()
    {
        var (provider, tenant) = BuildWithTenant(Home, Catalog, Products);
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<INavigationService>();
        var person = Person("catalog:products:read");
        var companyA = Guid.NewGuid();

        using (tenant.Change(new TenantInfo(companyA, "a")))
        {
            await navigation.SetOverrideAsync("catalog", new NavigationOverride(Title: "Stock", Order: -1));
            var menu = await navigation.GetMenuAsync(person);
            menu.Select(n => n.Title).Should().Equal(new[] { "Stock", "Home" }, because: "the company order puts the catalog first");
        }

        using (tenant.Change(new TenantInfo(Guid.NewGuid(), "b")))
            (await navigation.GetMenuAsync(person)).Select(n => n.Title).Should().Equal("Home", "Catalog");
    }

    [Fact]
    public async Task Override_MovesAnItemUnderAnother_AndRefusesALoop()
    {
        var (provider, tenant) = BuildWithTenant(Home, Catalog, Products, Orders);
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<INavigationService>();

        using (tenant.Change(new TenantInfo(Guid.NewGuid(), "acme")))
        {
            await navigation.SetOverrideAsync("catalog.orders", new NavigationOverride(ParentKey: "home"));
            var menu = await navigation.GetMenuAsync(Person("catalog:products:read", "catalog:orders:read"));
            menu.Single(n => n.Key == "home").Children.Select(n => n.Key).Should().Equal("catalog.orders");

            await FluentActions.Awaiting(() => navigation.SetOverrideAsync("catalog", new NavigationOverride(ParentKey: "catalog.products")))
                .Should().ThrowAsync<ValidationException>("a product cannot be the parent of its own group");
        }
    }

    [Fact]
    public async Task Override_RefusesUnknownItems_UnknownParents_AndBlankTitles()
    {
        var (provider, tenant) = BuildWithTenant(Home, Catalog);
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<INavigationService>();

        using (tenant.Change(new TenantInfo(Guid.NewGuid(), "acme")))
        {
            await FluentActions.Awaiting(() => navigation.SetOverrideAsync("nope", new NavigationOverride(Hidden: true)))
                .Should().ThrowAsync<NotFoundException>();
            await FluentActions.Awaiting(() => navigation.SetOverrideAsync("home", new NavigationOverride(ParentKey: "missing")))
                .Should().ThrowAsync<ValidationException>();
            await FluentActions.Awaiting(() => navigation.SetOverrideAsync("home", new NavigationOverride(Title: "  ")))
                .Should().ThrowAsync<ValidationException>();
        }
    }

    [Fact]
    public async Task Override_WithNoCompany_IsRefused_AndNeverBecomesTheInstallationDefault()
    {
        using var provider = Build(Home, Catalog);
        var navigation = provider.GetRequiredService<INavigationService>();

        await FluentActions.Awaiting(() => navigation.SetOverrideAsync("home", new NavigationOverride(Hidden: true)))
            .Should().ThrowAsync<ValidationException>();
        Keys(await navigation.GetMenuAsync(Person())).Should().Equal("home");
    }

    [Fact]
    public async Task Override_RemovedAgain_RestoresTheDeclaredItem()
    {
        var (provider, tenant) = BuildWithTenant(Home);
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<INavigationService>();

        using (tenant.Change(new TenantInfo(Guid.NewGuid(), "acme")))
        {
            await navigation.SetOverrideAsync("home", new NavigationOverride(Hidden: true));
            (await navigation.GetMenuAsync(Person())).Should().BeEmpty();

            await navigation.RemoveOverrideAsync("home");
            Keys(await navigation.GetMenuAsync(Person())).Should().Equal("home");
        }
    }

    [Fact]
    public void Definition_RefusesDuplicateKeys_MissingParents_Cycles_ExternalLinks_AndDeepNesting()
    {
        var duplicate = () => NavigationDefinition.Build([Home, Home]);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*declared more than once*");

        var missingParent = () => NavigationDefinition.Build([new NavigationItem("a", "A") { ParentKey = "missing" }]);
        missingParent.Should().Throw<InvalidOperationException>().WithMessage("*not declared*");

        var cycle = () => NavigationDefinition.Build([new NavigationItem("a", "A") { ParentKey = "b" }, new NavigationItem("b", "B") { ParentKey = "a" }]);
        cycle.Should().Throw<InvalidOperationException>().WithMessage("*its own ancestor*");

        var external = () => NavigationDefinition.Build([new NavigationItem("ext", "External") { Url = "https://evil.example/" }]);
        external.Should().Throw<InvalidOperationException>().WithMessage("*outside the application*");

        var networkPath = () => NavigationDefinition.Build([new NavigationItem("net", "Network") { Url = "//evil.example/" }]);
        networkPath.Should().Throw<InvalidOperationException>().WithMessage("*outside the application*");

        var badKey = () => NavigationDefinition.Build([new NavigationItem("bad key", "Bad")]);
        badKey.Should().Throw<InvalidOperationException>().WithMessage("*not a valid key*");

        var chain = Enumerable.Range(0, NavigationDefinition.MaxDepth + 1)
            .Select(i => new NavigationItem($"level{i}", $"Level {i}") { ParentKey = i == 0 ? null : $"level{i - 1}" })
            .ToList();
        var deep = () => NavigationDefinition.Build(chain);
        deep.Should().Throw<InvalidOperationException>().WithMessage("*deeper than*");
    }

    [Fact]
    public async Task Startup_RefusesAMenu_WhoseItemNamesAnUnregisteredPermission()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddAuthorization();
        services.AddSingleton<IPermissionRegistry>(new PermissionRegistry());
        services.AddModulusNavigation(navigation => navigation.Add(new NavigationItem("x", "X") { Permission = "not:registered" }));
        using var provider = services.BuildServiceProvider();

        var check = provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>().Single();

        await FluentActions.Awaiting(() => check.StartedAsync(default))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*not:registered*");
    }

    private sealed class ClaimPermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User.HasClaim("perm", requirement.Permission))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
