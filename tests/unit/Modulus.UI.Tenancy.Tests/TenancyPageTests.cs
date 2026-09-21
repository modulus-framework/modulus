using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.UI.Tenancy.Pages.Tenancy;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Tenancy.Tests;

/// <summary>
/// Spec for the tenant directory: ambient tenant description plus the
/// store-backed list (empty when the store cannot list).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenancyPageTests
{
    [Fact]
    public async Task Index_WithoutTenant_ShowsHostHint_AndEmptyList()
    {
        var store = Substitute.For<ITenantStore>();
        var model = new IndexModel(new CurrentTenant(), store, new TestLocalizer());

        await model.OnGetAsync(default);

        model.CurrentDescription.Should().Be("[Directory.Host]");
        model.Tenants.Should().BeEmpty();
    }

    [Fact]
    public async Task Index_WithTenant_ShowsSlugAndListsStoreTenants()
    {
        var store = Substitute.For<ITenantStore>();
        var tenants = new[]
        {
            new TenantInfo(Guid.NewGuid(), "acme", "Acme"),
            new TenantInfo(Guid.NewGuid(), "globex"),
        };
        store.ListAsync(Arg.Any<CancellationToken>()).Returns(tenants);
        var ambient = new CurrentTenant();
        var model = new IndexModel(ambient, store, new TestLocalizer());

        using (ambient.Change(new TenantInfo(Guid.NewGuid(), "acme")))
            await model.OnGetAsync(default);

        model.CurrentDescription.Should().StartWith("acme (");
        model.Tenants.Should().BeEquivalentTo(tenants);
    }

    [Fact]
    public async Task Details_KnownSlug_RendersTenant()
    {
        var store = Substitute.For<ITenantStore>();
        var tenant = new TenantInfo(Guid.NewGuid(), "acme", "Acme");
        store.FindBySlugAsync("acme", Arg.Any<CancellationToken>()).Returns(tenant);
        var model = new DetailsModel(store, new TestLocalizer());

        var result = await model.OnGetAsync("acme", default);

        result.Should().BeOfType<PageResult>();
        model.Tenant.Should().Be(tenant);
    }

    [Fact]
    public async Task Details_UnknownSlug_ReturnsNotFound()
    {
        var store = Substitute.For<ITenantStore>();
        store.FindBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TenantInfo?)null);
        var model = new DetailsModel(store, new TestLocalizer());

        var result = await model.OnGetAsync("ghost", default);

        result.Should().BeOfType<NotFoundResult>();
        model.Tenant.Should().BeNull();
    }
}
