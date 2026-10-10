namespace Modulus.Identity.EntityFrameworkCore;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>
/// Combined DbContext for ASP.NET Identity + OpenIddict entities.
/// Derive from this in your application to add module-specific DbSets.
/// </summary>
public class ModulusIdentityDbContext<TUser, TRole>(
    DbContextOptions options,
    ICurrentTenant currentTenant)
    : IdentityDbContext<TUser, TRole, Guid,
        IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>(options)
    where TUser : ModulusUser
    where TRole : ModulusRole
{
    /// <summary>The password hashes each account has used (see <see cref="ModulusPasswordHistoryEntry"/>).</summary>
    public DbSet<ModulusPasswordHistoryEntry> PasswordHistory => Set<ModulusPasswordHistoryEntry>();

    /// <summary>
    /// The most password hashes kept per account: the largest <c>Identity:Password:HistoryCount</c> allowed. Older ones
    /// are removed when the account is saved.
    /// </summary>
    internal const int PasswordHistoryRetained = 50;

    /// <summary>
    /// Records a password hash whenever an account row is saved with a new one. Every password path (creation, change,
    /// reset, invitation) ends in a saved account row, so this does not depend on which Identity method ran.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var accounts = RecordPasswordHashes();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        PrunePasswordHistory(accounts);
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var accounts = RecordPasswordHashes();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        await PrunePasswordHistoryAsync(accounts, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private List<Guid> RecordPasswordHashes()
    {
        var now = DateTimeOffset.UtcNow;
        List<Guid> accounts = [];

        // Snapshot first: adding history rows changes the tracker while it is enumerated.
        foreach (var entry in ChangeTracker.Entries<TUser>().ToList())
        {
            if (entry.Entity.PasswordHash is not { } hash)
                continue;

            var passwordSet = entry.State == EntityState.Added
                || (entry.State == EntityState.Modified && entry.Property(u => u.PasswordHash).IsModified);
            if (!passwordSet)
                continue;

            PasswordHistory.Add(new ModulusPasswordHistoryEntry
            {
                UserId = entry.Entity.Id,
                PasswordHash = hash,
                ChangedAt = now,
            });
            accounts.Add(entry.Entity.Id);
        }

        return accounts;
    }

    private void PrunePasswordHistory(List<Guid> accounts)
    {
        foreach (var userId in accounts.Distinct())
        {
            var keep = PasswordHistory.AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.Id)
                .Take(PasswordHistoryRetained)
                .Select(r => r.Id)
                .ToList();
            if (keep.Count < PasswordHistoryRetained)
                continue;

            PasswordHistory.Where(r => r.UserId == userId && !keep.Contains(r.Id)).ExecuteDelete();
        }
    }

    private async Task PrunePasswordHistoryAsync(List<Guid> accounts, CancellationToken ct)
    {
        foreach (var userId in accounts.Distinct())
        {
            var keep = await PasswordHistory.AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.Id)
                .Take(PasswordHistoryRetained)
                .Select(r => r.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (keep.Count < PasswordHistoryRetained)
                continue;

            await PasswordHistory.Where(r => r.UserId == userId && !keep.Contains(r.Id))
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TUser>(b =>
        {
            b.HasIndex(u => u.TenantId);
            b.HasIndex(u => u.Email);
            // Shop-floor sign-in looks an operator up by company + code. Not unique here: ordinary accounts have a null code
            // and not every provider ignores nulls in a unique index; ShopFloorAccountService checks uniqueness.
            b.HasIndex(u => new { u.TenantId, u.EmployeeCode });
            // TenantId is nullable (a host-level account belongs to no
            // tenant), so this can't use IHasTenantId / ModuleDbContext's
            // filter (which assumes a non-nullable TenantId) as-is. Same
            // fail-closed shape: the host sees everyone; a resolved tenant
            // sees only its own rows; an unresolved tenant (IsHost false,
            // TenantId null — a missing header, a background job that never
            // established one) sees nothing, including host-level accounts
            // (TenantId null never equals a non-null currentTenant.TenantId).
            b.HasQueryFilter(u =>
                currentTenant.IsHost
                || (currentTenant.TenantId != null && u.TenantId == currentTenant.TenantId));
        });

        builder.Entity<TRole>(b =>
        {
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => r.NormalizedName);
            b.HasQueryFilter(r =>
                currentTenant.IsHost
                || (currentTenant.TenantId != null && r.TenantId == currentTenant.TenantId));
        });

        // Password hashes for the reuse check and expiry. Removed with their account.
        builder.Entity<ModulusPasswordHistoryEntry>(b =>
        {
            b.ToTable("ModulusPasswordHistory");
            b.HasKey(h => h.Id);
            b.HasIndex(h => new { h.UserId, h.Id });
            b.HasOne<TUser>()
             .WithMany()
             .HasForeignKey(h => h.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Use OpenIddict EF Core tables
        builder.UseOpenIddict();
    }
}

/// <summary>
/// Convenience base for the standard ModulusUser/ModulusRole pair.
/// </summary>
public class ModulusIdentityDbContext(
    DbContextOptions options,
    ICurrentTenant currentTenant)
    : ModulusIdentityDbContext<ModulusUser, ModulusRole>(options, currentTenant);
