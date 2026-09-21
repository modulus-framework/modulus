using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Modulus.Identity.Abstractions;
using Modulus.UI.Users.Pages.Users;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Users.Tests;

/// <summary>
/// Spec for the user directory, details (activate/lock/roles), and creation:
/// unknown ids are 404, failed Identity operations surface as model errors,
/// and every mutation round-trips through the manager.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UsersPageTests
{
    private static IndexModel BuildIndex(UserManager<ModulusUser> users)
        => new(users, Microsoft.Extensions.Options.Options.Create(new UsersUiOptions()), new TestLocalizer());

    private static DetailsModel BuildDetails(
        UserManager<ModulusUser> users,
        RoleManager<ModulusRole> roles)
        => new(users, roles, new TestLocalizer());

    [Fact]
    public async Task Index_OrdersByUserName_WithLockoutFlags()
    {
        var bob = IdentityDoubles.User("bob");
        var alice = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        users.Users.Returns(new[] { bob, alice }.AsQueryable());
        users.IsLockedOutAsync(bob).Returns(true);

        var model = BuildIndex(users);
        await model.OnGetAsync(default);

        model.Rows.Select(r => r.User.UserName).Should().Equal("alice", "bob");
        model.Rows.Single(r => r.User.UserName == "bob").LockedOut.Should().BeTrue();
        model.Rows.Single(r => r.User.UserName == "alice").LockedOut.Should().BeFalse();
    }

    [Fact]
    public async Task Details_Known_LoadsRolesAndLockout()
    {
        var user = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        users.GetRolesAsync(user).Returns((IList<string>)["Admin"]);
        users.IsLockedOutAsync(user).Returns(true);
        var (roles, roleStore) = IdentityDoubles.RoleManager();
        roleStore.Roles.Returns(new[] { IdentityDoubles.Role("Admin") }.AsQueryable());

        var model = BuildDetails(users, roles);
        var result = await model.OnGetAsync(user.Id, default);

        result.Should().BeOfType<PageResult>();
        model.Account.Should().Be(user);
        model.AssignedRoles.Should().Equal("Admin");
        model.LockedOut.Should().BeTrue();
        model.AvailableRoles.Should().Equal("Admin");
    }

    [Fact]
    public async Task Details_Unknown_ReturnsNotFound()
    {
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(Arg.Any<string>()).Returns((ModulusUser?)null);
        var (roles, _) = IdentityDoubles.RoleManager();

        var result = await BuildDetails(users, roles).OnGetAsync(Guid.NewGuid(), default);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ToggleActive_FlipsFlag_Updates_Redirects()
    {
        var user = IdentityDoubles.User("alice", active: true);
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        users.UpdateAsync(Arg.Any<ModulusUser>()).Returns(IdentityResult.Success);
        var (roles, _) = IdentityDoubles.RoleManager();

        var result = await BuildDetails(users, roles).OnPostToggleActiveAsync(user.Id);

        result.Should().BeOfType<RedirectToPageResult>();
        user.IsActive.Should().BeFalse();
        await users.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task Lock_EnablesAndSetsEndDate_Redirects()
    {
        var user = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        var (roles, _) = IdentityDoubles.RoleManager();

        var result = await BuildDetails(users, roles).OnPostLockAsync(user.Id);

        result.Should().BeOfType<RedirectToPageResult>();
        await users.Received(1).SetLockoutEnabledAsync(user, true);
        await users.Received(1).SetLockoutEndDateAsync(
            user, Arg.Is<DateTimeOffset?>(d => d > DateTimeOffset.UtcNow.AddYears(1)));
    }

    [Fact]
    public async Task AddRole_BlankName_RendersError_WithoutCallingManager()
    {
        var user = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        var (roles, roleStore) = IdentityDoubles.RoleManager();
        roleStore.Roles.Returns(Array.Empty<ModulusRole>().AsQueryable());

        var model = BuildDetails(users, roles);
        var result = await model.OnPostAddRoleAsync(user.Id, "  ");

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        await users.DidNotReceiveWithAnyArgs().AddToRoleAsync(
            default!, default!);
    }

    [Fact]
    public async Task AddRole_Failure_SurfacesError()
    {
        var user = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        users.GetRolesAsync(user).Returns((IList<string>)[]);
        users.AddToRoleAsync(user, "Admin")
            .Returns(IdentityResult.Failed(new IdentityError { Description = "No such role." }));
        var (roles, roleStore) = IdentityDoubles.RoleManager();
        roleStore.Roles.Returns(Array.Empty<ModulusRole>().AsQueryable());

        var model = BuildDetails(users, roles);
        var result = await model.OnPostAddRoleAsync(user.Id, "Admin");

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
    }

    private static void AsHtmx(DetailsModel model)
    {
        model.PageContext = new PageContext(
            new ActionContext(
                new DefaultHttpContext(),
                new RouteData(),
                new PageActionDescriptor()));
        model.Request.Headers["HX-Request"] = "true";
    }

    private static string HxTrigger(DetailsModel model)
        => model.Response.Headers["HX-Trigger"].ToString();

    private static void StubLoadedUser(
        UserManager<ModulusUser> users,
        ModulusUser user)
    {
        users.GetRolesAsync(user).Returns((IList<string>)[]);
        users.IsLockedOutAsync(user).Returns(false);
    }

    private static RoleManager<ModulusRole> EmptyRoles()
    {
        var (roles, roleStore) = IdentityDoubles.RoleManager();
        roleStore.Roles.Returns(Array.Empty<ModulusRole>().AsQueryable());
        return roles;
    }

    [Fact]
    public async Task ToggleActive_Htmx_ReturnsUserCard_WithToast()
    {
        var user = IdentityDoubles.User("alice", active: true);
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        users.UpdateAsync(Arg.Any<ModulusUser>()).Returns(IdentityResult.Success);
        var roles = EmptyRoles();
        StubLoadedUser(users, user);

        var model = BuildDetails(users, roles);
        AsHtmx(model);
        var result = await model.OnPostToggleActiveAsync(user.Id);

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_UserCard");
        partial.Model.Should().Be(model);
        user.IsActive.Should().BeFalse();
        HxTrigger(model).Should().Contain("modulusToast");
    }

    [Fact]
    public async Task Lock_Htmx_ReturnsUserCard_WithToast()
    {
        var user = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        var roles = EmptyRoles();
        StubLoadedUser(users, user);

        var model = BuildDetails(users, roles);
        AsHtmx(model);
        var result = await model.OnPostLockAsync(user.Id);

        result.Should().BeOfType<PartialViewResult>()
            .Which.ViewName.Should().Be("_UserCard");
        HxTrigger(model).Should().Contain("modulusToast");
        await users.Received(1).SetLockoutEndDateAsync(
            user, Arg.Is<DateTimeOffset?>(d => d > DateTimeOffset.UtcNow.AddYears(1)));
    }

    [Fact]
    public async Task AddRole_Htmx_Valid_ReturnsRoleList_WithToast()
    {
        var user = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        users.AddToRoleAsync(user, "Admin").Returns(IdentityResult.Success);
        var roles = EmptyRoles();
        StubLoadedUser(users, user);

        var model = BuildDetails(users, roles);
        AsHtmx(model);
        var result = await model.OnPostAddRoleAsync(user.Id, "Admin");

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_RoleList");
        HxTrigger(model).Should().Contain("modulusToast");
        await users.Received(1).AddToRoleAsync(user, "Admin");
    }

    [Fact]
    public async Task RemoveRole_Htmx_ReturnsRoleList_WithToast()
    {
        var user = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        users.RemoveFromRoleAsync(user, "Admin").Returns(IdentityResult.Success);
        var roles = EmptyRoles();
        StubLoadedUser(users, user);

        var model = BuildDetails(users, roles);
        AsHtmx(model);
        var result = await model.OnPostRemoveRoleAsync(user.Id, "Admin");

        result.Should().BeOfType<PartialViewResult>()
            .Which.ViewName.Should().Be("_RoleList");
        HxTrigger(model).Should().Contain("modulusToast");
    }

    [Fact]
    public async Task AddRole_Htmx_Failure_ReturnsRoleList_WithErrors()
    {
        var user = IdentityDoubles.User("alice");
        var users = IdentityDoubles.UserManager();
        users.FindByIdAsync(user.Id.ToString()).Returns(user);
        users.AddToRoleAsync(user, "Admin")
            .Returns(IdentityResult.Failed(new IdentityError { Description = "No such role." }));
        var roles = EmptyRoles();
        StubLoadedUser(users, user);

        var model = BuildDetails(users, roles);
        AsHtmx(model);
        var result = await model.OnPostAddRoleAsync(user.Id, "Admin");

        var partial = result.Should().BeOfType<PartialViewResult>().Subject;
        partial.ViewName.Should().Be("_RoleList");
        partial.ViewData!.ModelState.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Create_Success_RedirectsToDetails()
    {
        var users = IdentityDoubles.UserManager();
        users.CreateAsync(Arg.Any<ModulusUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        var model = new CreateModel(users, new TestLocalizer());
        model.Input = new CreateModel.InputModel
        {
            UserName = "alice",
            Email = "alice@example.com",
            Password = "P@ssw0rd!",
        };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>();
        await users.Received(1).CreateAsync(
            Arg.Is<ModulusUser>(u => u.UserName == "alice" && u.Email == "alice@example.com"),
            "P@ssw0rd!");
    }

    [Fact]
    public async Task Create_Failure_RendersErrors()
    {
        var users = IdentityDoubles.UserManager();
        users.CreateAsync(Arg.Any<ModulusUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Too weak." }));
        var model = new CreateModel(users, new TestLocalizer());
        model.Input = new CreateModel.InputModel
        {
            UserName = "alice",
            Password = "weak",
        };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
    }
}
