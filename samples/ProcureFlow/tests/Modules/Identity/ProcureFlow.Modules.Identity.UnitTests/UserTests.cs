using FluentAssertions;
using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Enums;
using ProcureFlow.Modules.Identity.Domain.Events;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain.ValueObjects;
using Xunit;

namespace ProcureFlow.Modules.Identity.UnitTests.Domain;

public sealed class UserTests
{
    private static User CreateUser(bool emailConfirmed = false) =>
        User.Create(
            UserId.Create(),
            Email.Create("jane.doe@example.com").Value,
            UserName.Create("jane.doe"),
            "Jane",
            "Doe",
            UserType.User,
            emailConfirmed);

    [Fact]
    public void Create_NewUser_StartsPendingEmailVerification()
    {
        var user = CreateUser();

        user.Status.Should().Be(UserStatus.PendingEmailVerification);
        user.EmailConfirmed.Should().BeFalse();
        user.DomainEvents.Should().ContainSingle(e => e is UserCreatedDomainEvent);
    }

    [Fact]
    public void Create_WithEmailConfirmed_IsActiveImmediately()
    {
        var user = CreateUser(emailConfirmed: true);

        user.Status.Should().Be(UserStatus.Active);
        user.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public void VerifyEmail_MarksConfirmedAndActivates()
    {
        var user = CreateUser();
        var versionBefore = user.Version;

        user.VerifyEmail();

        user.EmailConfirmed.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Active);
        user.Version.Should().Be(versionBefore + 1);
        user.DomainEvents.Should().Contain(e => e is UserEmailVerifiedEvent);
    }

    [Fact]
    public void Suspend_SetsStatusAndRaisesEvent()
    {
        var user = CreateUser(emailConfirmed: true);

        user.Suspend("policy violation");

        user.Status.Should().Be(UserStatus.Suspended);
        user.DomainEvents.Should().Contain(e => e is UserSuspendedDomainEvent);
    }

    [Fact]
    public void Activate_AfterSuspend_RestoresActiveStatus()
    {
        var user = CreateUser(emailConfirmed: true);
        user.Suspend("policy violation");

        user.Activate();

        user.Status.Should().Be(UserStatus.Active);
        user.DomainEvents.Should().Contain(e => e is UserActivatedDomainEvent);
    }

    [Fact]
    public void AddRole_SameRoleTwice_IsIdempotent()
    {
        var user = CreateUser();
        var roleId = RoleId.Create();

        user.AddRole(roleId);
        user.AddRole(roleId);

        user.UserRoles.Should().ContainSingle(ur => ur.RoleId == roleId);
    }

    [Fact]
    public void RemoveRole_NotAssigned_DoesNothing()
    {
        var user = CreateUser();
        var versionBefore = user.Version;

        user.RemoveRole(RoleId.Create());

        user.Version.Should().Be(versionBefore);
        user.UserRoles.Should().BeEmpty();
    }

    [Fact]
    public void Delete_AnonymisesPersonalData()
    {
        var user = CreateUser(emailConfirmed: true);

        user.Delete("gdpr request");

        user.IsDeleted.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Deleted);
        user.FirstName.Should().Be("DELETED");
        user.LastName.Should().Be("USER");
        user.Email.Value.Should().Contain("deleted_");
        user.DomainEvents.Should().Contain(e => e is UserDeletedDomainEvent);
    }
}
