using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;

namespace Modulus.Identity;

/// <summary>One sign-in of a user: a token the auth server issued and has not yet seen expire or be revoked.</summary>
/// <param name="Id">The token entry id (pass it to <see cref="IUserSessionService.RevokeAsync"/>).</param>
/// <param name="Type"><c>access_token</c> or <c>refresh_token</c>; the refresh token is the long-lived one that keeps a session alive.</param>
/// <param name="ClientId">The application that was issued the token.</param>
/// <param name="CreatedAt">When it was issued.</param>
/// <param name="ExpiresAt">When it stops working on its own.</param>
public sealed record UserSession(string Id, string Type, string? ClientId, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt);

/// <summary>
/// Lists a user's active sessions and ends them: one, or all (a lost device, a leaving employee, a suspected compromise).
/// Admin pages and the account endpoints use it; it is the "revoke all sessions and credentials" of an account.
/// </summary>
public interface IUserSessionService
{
    /// <summary>The active tokens of <paramref name="userId"/>, newest first. Empty for an unknown user or when no token store is configured.</summary>
    Task<IReadOnlyList<UserSession>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Revokes one session of the user. False when the user has no such active session.</summary>
    Task<bool> RevokeAsync(Guid userId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Ends every session of the user: changes the security stamp (cookie sessions and refresh tokens fail their next check) and revokes
    /// every stored token (access tokens stop at once while <c>Identity:ValidateTokenEntries</c> is on). Observers are told.
    /// </summary>
    /// <returns>The number of tokens revoked; -1 when the user is unknown.</returns>
    Task<int> RevokeAllAsync(Guid userId, string reason, CancellationToken ct = default);
}

internal sealed class UserSessionService<TUser>(
    UserManager<TUser> users,
    IServiceProvider services,
    ILogger<UserSessionService<TUser>> logger) : IUserSessionService
    where TUser : ModulusUser
{
    public async Task<IReadOnlyList<UserSession>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        if (services.GetService<IOpenIddictTokenManager>() is not { } tokens)
            return [];

        var now = DateTimeOffset.UtcNow;
        var sessions = new List<UserSession>();
        await foreach (var token in tokens.FindBySubjectAsync(userId.ToString(), ct))
        {
            if (await tokens.GetStatusAsync(token, ct) != OpenIddictConstants.Statuses.Valid)
                continue;
            var expires = await tokens.GetExpirationDateAsync(token, ct);
            if (expires is { } end && end <= now)
                continue;

            // Stored either as "access_token" or in its URN form (urn:ietf:params:oauth:token-type:access_token).
            var type = SessionType(await tokens.GetTypeAsync(token, ct));
            if (type is null)
                continue;

            sessions.Add(new UserSession(
                await tokens.GetIdAsync(token, ct) ?? "", type, await ClientOfAsync(token, ct), await tokens.GetCreationDateAsync(token, ct), expires));
        }

        return [.. sessions.OrderByDescending(s => s.CreatedAt)];
    }

    public async Task<bool> RevokeAsync(Guid userId, string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (services.GetService<IOpenIddictTokenManager>() is not { } tokens)
            return false;

        await foreach (var token in tokens.FindBySubjectAsync(userId.ToString(), ct))
        {
            if (await tokens.GetIdAsync(token, ct) != sessionId)
                continue;

            var revoked = await tokens.TryRevokeAsync(token, ct);
            if (revoked)
                await NotifyAsync(userId, "session.revoked", ct);
            return revoked;
        }

        return false;
    }

    public async Task<int> RevokeAllAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return -1;

        // A new stamp ends cookie sessions and fails the stamp check of every refresh token, whatever the token store holds.
        await users.UpdateSecurityStampAsync(user);

        var revoked = 0;
        if (services.GetService<IOpenIddictTokenManager>() is { } tokens)
        {
            await foreach (var token in tokens.FindBySubjectAsync(userId.ToString(), ct))
            {
                if (await tokens.TryRevokeAsync(token, ct))
                    revoked++;
            }
        }

        logger.LogInformation("Ended every session of user {UserId} ({Revoked} tokens revoked): {Reason}", userId, revoked, reason);
        await NotifyAsync(userId, "sessions.revoked", ct, user.TenantId);
        return revoked;
    }

    private static string? SessionType(string? stored)
        => stored is null ? null
            : stored.EndsWith(OpenIddictConstants.TokenTypeHints.AccessToken, StringComparison.Ordinal) ? OpenIddictConstants.TokenTypeHints.AccessToken
            : stored.EndsWith(OpenIddictConstants.TokenTypeHints.RefreshToken, StringComparison.Ordinal) ? OpenIddictConstants.TokenTypeHints.RefreshToken
            : null;

    private async Task<string?> ClientOfAsync(object token, CancellationToken ct)
    {
        var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
        if (await tokens.GetApplicationIdAsync(token, ct) is not { } applicationId
            || services.GetService<IOpenIddictApplicationManager>() is not { } applications
            || await applications.FindByIdAsync(applicationId, ct) is not { } application)
            return null;

        return await applications.GetClientIdAsync(application, ct);
    }

    private async Task NotifyAsync(Guid userId, string reason, CancellationToken ct, Guid? tenantId = null)
    {
        var observers = services.GetServices<IAccessChangeObserver>();
        await observers.NotifyAccessChangedAsync(
            new AccessChange { Kind = AccessChangeKinds.Account, Reason = reason, UserId = userId, TenantId = tenantId }, logger, ct);
        services.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = reason,
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = tenantId,
            Actor = userId.ToString(),
            Target = $"user:{userId}",
        });
    }
}
