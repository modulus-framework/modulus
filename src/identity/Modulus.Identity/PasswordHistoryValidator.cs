namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;

/// <summary>
/// Refuses a new password that matches one of the account's last <c>Identity:Password:HistoryCount</c> passwords. It is an
/// Identity <see cref="IPasswordValidator{TUser}"/>, so it runs for every path that sets a password (creation, change,
/// reset, invitation) and reports through the usual <see cref="IdentityResult"/>. Off when <c>HistoryCount</c> is 0.
/// </summary>
internal sealed class PasswordHistoryValidator<TUser>(
    IOptions<ModulusIdentityOptions> options,
    IPasswordHistoryStore history)
    : IPasswordValidator<TUser>
    where TUser : ModulusUser
{
    public async Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string? password)
    {
        var count = options.Value.Password.HistoryCount;

        // A new account (no stored history) and a call without a password have nothing to compare against.
        if (count <= 0 || password is null || user.Id == Guid.Empty)
            return IdentityResult.Success;

        var hashes = await history.GetRecentHashesAsync(user.Id, count, CancellationToken.None).ConfigureAwait(false);
        foreach (var hash in hashes)
        {
            if (manager.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "PasswordReused",
                    Description = $"The password must not be one of your last {count} passwords.",
                });
            }
        }

        return IdentityResult.Success;
    }
}
