using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.UI.Users.Pages.Roles;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Users.Tests;

/// <summary>
/// Spec for the role catalog: ordered listing, validated create, and delete
/// with 404 for unknown ids — all through a real
/// <see cref="RoleManager{ModulusRole}"/> over a fake store.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RolesPageTests
{
    private static IndexModel Build(RoleManager<ModulusRole> roles)
        => new(roles, Options.Create(new UsersUiOptions()), new TestLocalizer());

    [Fact]
    public async Task Index_ListsRolesOrderedByName()
    {
        var (roles, store) = IdentityDoubles.RoleManager();
        store.Roles.Returns(
            new[] { IdentityDoubles.Role("Viewer"), IdentityDoubles.Role("Admin") }.AsQueryable());

        var model = Build(roles);
        await model.OnGetAsync(default);

        model.Rows.Select(r => r.Name).Should().Equal("Admin", "Viewer");
    }

    [Fact]
    public async Task Create_BlankName_RendersError_WithoutCallingStore()
    {
        var (roles, store) = IdentityDoubles.RoleManager();

        var model = Build(roles);
        var result = await model.OnPostCreateAsync("  ");

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        await store.DidNotReceiveWithAnyArgs().CreateAsync(
            default!, default);
    }

    [Fact]
    public async Task Create_Valid_CreatesAndRedirects()
    {
        var (roles, store) = IdentityDoubles.RoleManager();
        store.CreateAsync(Arg.Any<ModulusRole>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);

        var result = await Build(roles).OnPostCreateAsync("Editor");

        result.Should().BeOfType<RedirectToPageResult>();
        await store.Received(1).CreateAsync(
            Arg.Is<ModulusRole>(r => r.Name == "Editor"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        var (roles, store) = IdentityDoubles.RoleManager();
        store.FindByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ModulusRole?)null);

        var result = await Build(roles).OnPostDeleteAsync(Guid.NewGuid());

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Delete_Known_DeletesAndRedirects()
    {
        var role = IdentityDoubles.Role("Editor");
        var (roles, store) = IdentityDoubles.RoleManager();
        store.FindByIdAsync(role.Id.ToString(), Arg.Any<CancellationToken>()).Returns(role);
        store.DeleteAsync(role, Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);

        var result = await Build(roles).OnPostDeleteAsync(role.Id);

        result.Should().BeOfType<RedirectToPageResult>();
        await store.Received(1).DeleteAsync(role, Arg.Any<CancellationToken>());
    }

    private static void AsHtmx(IndexModel model)
    {
        model.PageContext = new PageContext(
            new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new PageActionDescriptor()));
        model.Request.Headers["HX-Request"] = "true";
    }

    private static string HxTrigger(IndexModel model)
        => model.Response.Headers["HX-Trigger"].ToString();

    [Fact]
    public async Task Delete_Htmx_Known_ReturnsRoleTable_WithToast()
    {
        var role = IdentityDoubles.Role("Editor");
        var (roles, store) = IdentityDoubles.RoleManager();
        store.FindByIdAsync(role.Id.ToString(), Arg.Any<CancellationToken>()).Returns(role);
        store.DeleteAsync(role, Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);
        store.Roles.Returns(Array.Empty<ModulusRole>().AsQueryable());

        var model = Build(roles);
        AsHtmx(model);
        var result = await model.OnPostDeleteAsync(role.Id);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_RoleTable");
        partial.Model.Should().Be(model);
        HxTrigger(model).Should().Contain("modulusToast");
        await store.Received(1).DeleteAsync(role, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_Htmx_Failure_ReturnsRoleTable_WithErrors()
    {
        var role = IdentityDoubles.Role("Editor");
        var (roles, store) = IdentityDoubles.RoleManager();
        store.FindByIdAsync(role.Id.ToString(), Arg.Any<CancellationToken>()).Returns(role);
        store.DeleteAsync(role, Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "In use." }));
        store.Roles.Returns(new[] { role }.AsQueryable());

        var model = Build(roles);
        AsHtmx(model);
        var result = await model.OnPostDeleteAsync(role.Id);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_RoleTable");
        partial.ViewData!.ModelState.IsValid.Should().BeFalse();
    }
}
