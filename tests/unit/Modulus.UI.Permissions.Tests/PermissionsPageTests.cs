using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.UI.Permissions.Pages.Permissions;
using NSubstitute;
using Xunit;

namespace Modulus.UI.Permissions.Tests;

/// <summary>
/// Spec for the permission catalog (module-prefix grouping) and the
/// holder-grant viewer (holder parsing, role pass-through, read-only).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PermissionsPageTests
{
    [Fact]
    public void Index_GroupsByModulePrefix_UngroupedUnderGeneral()
    {
        var registry = Substitute.For<IPermissionRegistry>();
        registry.GetAll().Returns(
        [
            new PermissionDefinition("orders:create", "Create", []),
            new PermissionDefinition("orders:read", "Read", []),
            new PermissionDefinition("tenancy:view", "View", []),
            new PermissionDefinition("misc", "Misc", []),
        ]);
        var model = new IndexModel(registry, new TestLocalizer());

        model.OnGet();

        model.Groups.Should().HaveCount(3);
        model.Groups.Select(g => g.Module).Should().BeEquivalentTo("General", "orders", "tenancy");
        model.Groups.Single(g => g.Module == "orders").Permissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Holder_RoleLookup_ReturnsGrants()
    {
        var grants = Substitute.For<IPermissionGrantStore>();
        grants.GetGrants(Arg.Any<PrincipalGrantQuery>()).Returns(
        [
            new PermissionGrant(GrantHolderType.Role, "admin", "orders:create", PermissionGrantType.Allow),
        ]);
        var model = new HolderModel(grants, new TestLocalizer())
        {
            Query = new HolderModel.QueryModel { HolderType = "Role", Holder = "admin" },
        };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.Searched.Should().BeTrue();
        model.Result.Should().ContainSingle().Which.Permission.Should().Be("orders:create");
    }

    [Fact]
    public async Task Holder_UserLookup_PassesUserIdAndRoles()
    {
        var grants = Substitute.For<IPermissionGrantStore>();
        grants.GetGrants(Arg.Any<PrincipalGrantQuery>()).Returns([]);
        var userId = Guid.NewGuid();
        var model = new HolderModel(grants, new TestLocalizer())
        {
            Query = new HolderModel.QueryModel
            {
                HolderType = "User",
                Holder = userId.ToString(),
                Roles = "admin, support",
            },
        };

        await model.OnPostAsync();

        grants.Received(1).GetGrants(Arg.Is<PrincipalGrantQuery>(q =>
            q.UserId == userId && q.Roles.Count == 2 && q.Roles.Contains("admin")));
    }

    [Fact]
    public async Task Holder_UnknownHolderType_RendersError_WithoutStoreCall()
    {
        var grants = Substitute.For<IPermissionGrantStore>();
        var model = new HolderModel(grants, new TestLocalizer())
        {
            Query = new HolderModel.QueryModel { HolderType = "Group", Holder = "x" },
        };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.Searched.Should().BeFalse();
        model.ModelState.IsValid.Should().BeFalse();
        grants.DidNotReceive().GetGrants(Arg.Any<PrincipalGrantQuery>());
    }

    [Fact]
    public async Task Holder_UserWithNonGuid_RendersError_WithoutStoreCall()
    {
        var grants = Substitute.For<IPermissionGrantStore>();
        var model = new HolderModel(grants, new TestLocalizer())
        {
            Query = new HolderModel.QueryModel { HolderType = "User", Holder = "not-a-guid" },
        };

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.Searched.Should().BeFalse();
        grants.DidNotReceive().GetGrants(Arg.Any<PrincipalGrantQuery>());
    }
}
