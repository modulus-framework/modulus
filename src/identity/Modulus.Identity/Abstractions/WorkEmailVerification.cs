namespace Modulus.Identity.Abstractions;

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

/// <summary>Settings of the work-e-mail confirmation (<c>Identity:WorkEmail</c>).</summary>
public sealed class WorkEmailVerificationOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Identity:WorkEmail";

    /// <summary>How long a confirmation link works. Default 30 minutes.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>The least time between two confirmation requests for one account, so the mail cannot be used as a sender. Default 60 seconds.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Failed confirmation attempts allowed on one account before confirmation is refused until a new request. Default 5.</summary>
    public int MaxFailedConfirmations { get; set; } = 5;
}

/// <summary>What a work-e-mail request did.</summary>
public enum WorkEmailRequestOutcome
{
    /// <summary>The address is pending; the caller sends <see cref="WorkEmailRequest.Token"/> to it.</summary>
    Sent,
    /// <summary>The address is already the verified work e-mail; nothing to confirm.</summary>
    Unchanged,
    /// <summary>A request was made within the cooldown; nothing was sent.</summary>
    TooSoon,
    /// <summary>The account could not be saved (a concurrent change); nothing was sent.</summary>
    Failed,
}

/// <summary>The outcome of a request, with the one-time token to mail when it was <see cref="WorkEmailRequestOutcome.Sent"/>.</summary>
public sealed record WorkEmailRequest(WorkEmailRequestOutcome Outcome, string? Token, IdentityResult? Result);

/// <summary>What confirming a work-e-mail link did.</summary>
public enum WorkEmailConfirmOutcome
{
    /// <summary>The pending address is now the verified work e-mail.</summary>
    Verified,
    /// <summary>The link is not one this server issued for this account and purpose, or it is damaged.</summary>
    Invalid,
    /// <summary>The link's time has passed. A new one must be requested.</summary>
    Expired,
    /// <summary>The link was already used: the address is already confirmed.</summary>
    Used,
    /// <summary>A newer link or a different address replaced this one. Only the newest link works.</summary>
    Superseded,
    /// <summary>Too many failed attempts on this account since the newest request; refused until a new request is made.</summary>
    TooManyAttempts,
    /// <summary>The account could not be saved (a concurrent change); nothing changed.</summary>
    Failed,
}

/// <summary>
/// The work-e-mail verification (premodule 3). A work e-mail is a contact address; it becomes the verified one only when its owner follows
/// a link sent to it. The link is a time-limited, tamper-proof token (ASP.NET Data Protection, with a purpose of its own, so an
/// e-mail-change or sign-up token cannot confirm it) that names the account, the pending address and a per-request stamp kept on the
/// account. Issuing a new link or a new address replaces the stamp, and confirming clears it, so each link works once and only the
/// newest one works. The sign-in e-mail is never touched.
/// </summary>
public sealed class WorkEmailVerifier<TUser>(
    UserManager<TUser> users,
    IDataProtectionProvider protection,
    IOptions<WorkEmailVerificationOptions> options,
    TimeProvider clock)
    where TUser : ModulusUser
{
    /// <summary>The Data Protection purpose. Changing it invalidates every link that is out.</summary>
    internal const string Purpose = "Modulus.Identity.WorkEmailVerification.v1";

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>
    /// Makes <paramref name="address"/> the pending work e-mail of <paramref name="user"/> and returns the link token to mail to it. The
    /// caller validates the address first. An address already verified is left alone; a request inside the cooldown is refused.
    /// </summary>
    public async Task<WorkEmailRequest> RequestAsync(TUser user, string address)
    {
        if (string.Equals(user.WorkEmail, address, StringComparison.OrdinalIgnoreCase) && user.PendingWorkEmail is null)
            return new WorkEmailRequest(WorkEmailRequestOutcome.Unchanged, null, null);

        var now = clock.GetUtcNow();
        if (user.WorkEmailRequestedAt is { } last && now - last < options.Value.Cooldown)
            return new WorkEmailRequest(WorkEmailRequestOutcome.TooSoon, null, null);

        // A new stamp invalidates every earlier link, including one for the same address.
        user.PendingWorkEmail = address;
        user.WorkEmailVerificationStamp = Guid.NewGuid().ToString("N");
        user.WorkEmailRequestedAt = now;
        user.ConfirmationFailures = 0;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
            return new WorkEmailRequest(WorkEmailRequestOutcome.Failed, null, result);

        // The lifetime is measured by the protector's own clock, which is the one that checks it when the link is used.
        var token = _protector.Protect(Payload(user.Id, user.WorkEmailVerificationStamp, address, now + options.Value.Lifetime));
        return new WorkEmailRequest(WorkEmailRequestOutcome.Sent, token, result);
    }

    /// <summary>
    /// Confirms the link for <paramref name="callerId"/>'s own account. The link must name that account, the current stamp and the pending
    /// address. On success the address becomes the verified work e-mail and the stamp is cleared, so the link cannot be used again.
    /// </summary>
    public async Task<WorkEmailConfirmOutcome> ConfirmAsync(Guid callerId, string token)
    {
        var user = await users.FindByIdAsync(callerId.ToString());
        if (user is null)
            return WorkEmailConfirmOutcome.Invalid;
        if (user.ConfirmationFailures >= options.Value.MaxFailedConfirmations)
            return WorkEmailConfirmOutcome.TooManyAttempts;

        var check = Check(user, callerId, token);
        if (check != WorkEmailConfirmOutcome.Verified)
        {
            // Only links that were guessed or forged count toward the limit. An expired or already-used link is an honest mistake.
            if (check is WorkEmailConfirmOutcome.Invalid or WorkEmailConfirmOutcome.Superseded)
            {
                user.ConfirmationFailures++;
                await users.UpdateAsync(user);
            }
            return check;
        }

        // The verified address is replaced only now, by the pending one the link named.
        user.WorkEmail = user.PendingWorkEmail;
        user.PendingWorkEmail = null;
        user.WorkEmailVerificationStamp = null;
        user.ConfirmationFailures = 0;
        var result = await users.UpdateAsync(user);
        return result.Succeeded ? WorkEmailConfirmOutcome.Verified : WorkEmailConfirmOutcome.Failed;
    }

    /// <summary>
    /// Decides what a link is. The payload is authenticated (a link that was not issued for this purpose fails the protection), and the
    /// expiry is checked against the injected clock. Then the link must name this account and carry the current stamp and pending address.
    /// </summary>
    private WorkEmailConfirmOutcome Check(TUser user, Guid callerId, string token)
    {
        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (CryptographicException)
        {
            return WorkEmailConfirmOutcome.Invalid;
        }

        // The expiry is the last field; the address is the rest after the stamp, so an address may itself contain '|'.
        var cut = payload.LastIndexOf('|');
        if (cut < 0 || !DateTimeOffset.TryParse(payload[(cut + 1)..], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
            return WorkEmailConfirmOutcome.Invalid;
        var parts = payload[..cut].Split('|', 3);
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var userId) || userId != callerId)
            return WorkEmailConfirmOutcome.Invalid;

        if (clock.GetUtcNow() >= expiresAt)
            return WorkEmailConfirmOutcome.Expired;
        if (user.PendingWorkEmail is null && user.WorkEmailVerificationStamp is null)
            return WorkEmailConfirmOutcome.Used;
        if (user.PendingWorkEmail is null || user.WorkEmailVerificationStamp is null)
            return WorkEmailConfirmOutcome.Superseded;
        if (!string.Equals(user.WorkEmailVerificationStamp, parts[1], StringComparison.Ordinal)
            || !string.Equals(user.PendingWorkEmail, parts[2], StringComparison.OrdinalIgnoreCase))
            return WorkEmailConfirmOutcome.Superseded;
        return WorkEmailConfirmOutcome.Verified;
    }

    /// <summary>Clears the pending address and every link that is out (the account's work e-mail is removed, or the request is withdrawn).</summary>
    public async Task<IdentityResult> ClearAsync(TUser user)
    {
        user.PendingWorkEmail = null;
        // A new stamp makes every link out superseded, which is not the same as used.
        user.WorkEmailVerificationStamp = Guid.NewGuid().ToString("N");
        user.ConfirmationFailures = 0;
        return await users.UpdateAsync(user);
    }

    private static string Payload(Guid userId, string stamp, string address, DateTimeOffset expiresAt)
        => $"{userId:D}|{stamp}|{address}|{expiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}";
}
