namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>
/// Reads the display names of accounts from the ASP.NET Core Identity store (<see cref="IUserDisplayNameDirectory"/>). Accounts are
/// host-level and their query filter is fail-closed for a company, so the lookup runs in the host context. It reads the display name only:
/// never the login name or the email address.
/// </summary>
internal sealed class IdentityUserDisplayNameDirectory<TUser>(UserManager<TUser> users, ICurrentTenant tenant) : IUserDisplayNameDirectory
    where TUser : ModulusUser
{
    public async ValueTask<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
            return new Dictionary<Guid, string>();

        var ids = userIds.Distinct().ToArray();
        using var _ = tenant.Change(null);
        var rows = await users.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync(ct);

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.DisplayName))
            .ToDictionary(r => r.Id, r => r.DisplayName!.Trim());
    }
}
