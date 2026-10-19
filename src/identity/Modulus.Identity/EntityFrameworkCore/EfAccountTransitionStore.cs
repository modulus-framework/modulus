using Microsoft.EntityFrameworkCore;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IAccountTransitionStore"/>, over the same context as the Identity store.
/// </summary>
internal sealed class EfAccountTransitionStore<TUser>(DbContext db) : IAccountTransitionStore
    where TUser : ModulusUser
{
    public async Task<AccountLifecycle?> GetStoredLifecycleAsync(Guid userId, CancellationToken ct = default)
        => await db.Set<TUser>().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (AccountLifecycle?)u.Lifecycle)
            .FirstOrDefaultAsync(ct);

    public void Record(ModulusAccountTransition transition) => db.Set<ModulusAccountTransition>().Add(transition);
}
