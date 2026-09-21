using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using NSubstitute;

namespace Modulus.UI.Users.Tests;

/// <summary>
/// Shared Identity doubles: a <see cref="UserManager{ModulusUser}"/> proxy
/// with real constructor dependencies (only the virtuals under test are
/// stubbed per-test), a real <see cref="RoleManager{ModulusRole}"/> over a
/// fake role store, and a combined user-store interface so a single
/// substitute covers every store capability the managers touch.
/// Public so the Castle proxy generator (NSubstitute) can implement the
/// store interfaces.
/// </summary>
public static class IdentityDoubles
{
    public interface IUserTestStore
        : IQueryableUserStore<ModulusUser>,
          IUserRoleStore<ModulusUser>,
          IUserPasswordStore<ModulusUser>,
          IUserLockoutStore<ModulusUser>
    {
    }

    public interface IRoleTestStore : IQueryableRoleStore<ModulusRole>
    {
    }

    public static UserManager<ModulusUser> UserManager(IUserTestStore? store = null)
        => Substitute.For<UserManager<ModulusUser>>(
            store ?? Substitute.For<IUserTestStore>(),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ModulusUser>(),
            Array.Empty<IUserValidator<ModulusUser>>(),
            Array.Empty<IPasswordValidator<ModulusUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ModulusUser>>.Instance);

    public static (RoleManager<ModulusRole> Manager, IRoleTestStore Store) RoleManager()
    {
        var store = Substitute.For<IRoleTestStore>();
        var manager = new RoleManager<ModulusRole>(
            store,
            Array.Empty<IRoleValidator<ModulusRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<ModulusRole>>.Instance);
        return (manager, store);
    }

    public static ModulusUser User(string userName, bool active = true)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = $"{userName}@example.com",
            IsActive = active,
        };

    public static ModulusRole Role(string name)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
        };
}
