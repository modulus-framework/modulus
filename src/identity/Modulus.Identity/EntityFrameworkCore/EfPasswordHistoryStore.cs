using Microsoft.EntityFrameworkCore;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IPasswordHistoryStore"/>, over the same context as the Identity store.
/// </summary>
internal sealed class EfPasswordHistoryStore(DbContext db) : IPasswordHistoryStore
{
    private DbSet<ModulusPasswordHistoryEntry> Rows => db.Set<ModulusPasswordHistoryEntry>();

    public async Task<IReadOnlyList<string>> GetRecentHashesAsync(Guid userId, int count, CancellationToken ct)
        => await Rows.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Id)
            .Take(count)
            .Select(r => r.PasswordHash)
            .ToListAsync(ct);

    public async Task<DateTimeOffset?> GetLastChangedAsync(Guid userId, CancellationToken ct)
        => await Rows.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.Id)
            .Select(r => (DateTimeOffset?)r.ChangedAt)
            .FirstOrDefaultAsync(ct);
}
