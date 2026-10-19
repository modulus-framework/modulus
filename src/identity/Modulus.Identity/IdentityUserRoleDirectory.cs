namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>Reads a user's roles from the ASP.NET Core Identity store (<see cref="IUserRoleDirectory"/>).</summary>
internal sealed class IdentityUserRoleDirectory<TUser>(UserManager<TUser> users) : IUserRoleDirectory
    where TUser : class
{
    public async ValueTask<IReadOnlyCollection<string>?> GetRolesAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        return [.. await users.GetRolesAsync(user)];
    }

    public async ValueTask<bool> MayStartAccessAsync(Guid userId, CancellationToken ct = default)
        => await users.FindByIdAsync(userId.ToString()) is ModulusUser user && user.MayStartNewAccess();
}
