using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity;

/// <summary>What a user needs to add an authenticator app.</summary>
/// <param name="SharedKey">The secret, shown in groups for typing by hand.</param>
/// <param name="AuthenticatorUri">The <c>otpauth://</c> URI to render as a QR code.</param>
public sealed record TwoFactorSetup(string SharedKey, string AuthenticatorUri);

/// <summary>Authenticator-app (TOTP) enrolment for an account. Sign-in enforcement lives in the password grant (<c>mfa_code</c>).</summary>
public interface IUserTwoFactorService
{
    /// <summary>Whether two-factor is on for the user; false for an unknown user.</summary>
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Starts enrolment: a (new) shared key and the URI for the authenticator app. Nothing is enforced until <see cref="EnableAsync"/>. Null for an unknown user.</summary>
    Task<TwoFactorSetup?> BeginSetupAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Turns two-factor on when <paramref name="code"/> is a valid current code from the app (proof the app holds the key) and returns ten
    /// single-use recovery codes, shown once. Null when the code is wrong or the user is unknown.
    /// </summary>
    Task<IReadOnlyList<string>?> EnableAsync(Guid userId, string code, CancellationToken ct = default);

    /// <summary>Turns two-factor off, forgets the key, and ends every other session of the account. False for an unknown user.</summary>
    Task<bool> DisableAsync(Guid userId, CancellationToken ct = default);
}

internal sealed class UserTwoFactorService<TUser>(
    UserManager<TUser> users,
    IServiceProvider services,
    IHostEnvironment environment) : IUserTwoFactorService
    where TUser : ModulusUser
{
    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default)
        => await users.FindByIdAsync(userId.ToString()) is { } user && await users.GetTwoFactorEnabledAsync(user);

    public async Task<TwoFactorSetup?> BeginSetupAsync(Guid userId, CancellationToken ct = default)
    {
        if (await users.FindByIdAsync(userId.ToString()) is not { } user)
            return null;

        // A fresh key each time: an earlier, unconfirmed enrolment can not be completed with a stale code.
        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        var label = UrlEncoder.Default.Encode(await users.GetEmailAsync(user) ?? user.UserName ?? userId.ToString());
        var issuer = UrlEncoder.Default.Encode(environment.ApplicationName);
        return new TwoFactorSetup(
            string.Join(' ', Chunk(key.ToLowerInvariant(), 4)),
            string.Create(CultureInfo.InvariantCulture, $"otpauth://totp/{issuer}:{label}?secret={key}&issuer={issuer}&digits=6"));
    }

    public async Task<IReadOnlyList<string>?> EnableAsync(Guid userId, string code, CancellationToken ct = default)
    {
        if (await users.FindByIdAsync(userId.ToString()) is not { } user || string.IsNullOrWhiteSpace(code))
            return null;

        var digits = code.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, digits))
            return null;

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        Record(user, "2fa.enabled");
        return [.. codes ?? []];
    }

    public async Task<bool> DisableAsync(Guid userId, CancellationToken ct = default)
    {
        if (await users.FindByIdAsync(userId.ToString()) is not { } user)
            return false;

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        // Weakening the account: end the other sessions so a stolen one does not outlive the change.
        if (services.GetService<IUserSessionService>() is { } sessions)
            await sessions.RevokeAllAsync(userId, "two-factor disabled", ct);
        Record(user, "2fa.disabled");
        return true;
    }

    private void Record(TUser user, string action)
        => services.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = user.TenantId,
            Actor = user.Id.ToString(),
            Target = $"user:{user.Id}",
        });

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
