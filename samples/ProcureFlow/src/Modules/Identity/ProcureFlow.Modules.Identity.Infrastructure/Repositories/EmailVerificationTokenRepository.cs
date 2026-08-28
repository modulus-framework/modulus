using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Modules.Identity.Infrastructure.Database;
using ProcureFlow.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ProcureFlow.Modules.Identity.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for email verification tokens.
/// </summary>
internal sealed class EmailVerificationTokenRepository(IdentityDbContext context) : IEmailVerificationTokenRepository
{
    public async Task<EmailVerificationToken?> GetLatestUnusedTokenAsync(Guid userId, CancellationToken ct = default)
    {
        var userIdValueObject = new UserId(userId);
        return await context.EmailVerificationTokens
            .AsTracking()
            .Where(t => t.UserId == userIdValueObject && !t.IsUsed)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<EmailVerificationToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        return await context.EmailVerificationTokens
            .AsTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }

    public async Task AddAsync(EmailVerificationToken token, CancellationToken ct = default)
    {
        await context.EmailVerificationTokens.AddAsync(token, ct);
    }

    public async Task InvalidateAllUserTokensAsync(Guid userId, CancellationToken ct = default)
    {
        var userIdValueObject = new UserId(userId);
        List<EmailVerificationToken> unusedTokens = await context.EmailVerificationTokens
            .Where(t => t.UserId == userIdValueObject && !t.IsUsed)
            .ToListAsync(ct);

        foreach (EmailVerificationToken token in unusedTokens)
        {
            // Mark as used without raising domain events
            token.MarkAsUsed();
        }
    }

    public void Update(EmailVerificationToken token)
    {
        context.EmailVerificationTokens.Update(token);
    }

    public void Delete(EmailVerificationToken token)
    {
        context.EmailVerificationTokens.Remove(token);
    }
}
