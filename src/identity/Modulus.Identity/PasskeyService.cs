namespace Modulus.Identity;

using System.Globalization;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>
/// Passkeys over Fido2 (WebAuthn). A ceremony's options live in a database row until the answer arrives, so any replica can
/// finish what another started, and the row is deleted in one conditional statement when it is used: a signed answer cannot
/// be replayed. User verification is always required (the passkey itself is a second factor: possession plus biometric or
/// PIN), and a counter that stops growing is treated as a cloned authenticator.
/// </summary>
internal sealed class PasskeyService<TContext, TUser>(
    TContext db,
    UserManager<TUser> userManager,
    IFido2 fido2,
    IOptions<PasskeyOptions> options,
    TimeProvider? clock = null,
    ISecurityAuditLog? audit = null) : IPasskeyService
    where TContext : DbContext
    where TUser : ModulusUser, new()
{
    private const string Register = "register";
    private const string Login = "login";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private PasskeyOptions Settings => options.Value;

    public async Task<PasskeyCeremony?> BeginRegistrationAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not { IsActive: true })
            return null;

        var existing = await db.Set<ModulusPasskey>().AsNoTracking().Where(p => p.UserId == userId).Select(p => p.CredentialId).ToListAsync(ct);
        if (existing.Count >= Settings.MaxPasskeysPerUser)
            throw new InvalidOperationException($"An account may have at most {Settings.MaxPasskeysPerUser} passkeys.");

        var created = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = userId.ToByteArray(), Name = user.UserName ?? user.Email ?? userId.ToString("N"), DisplayName = user.FullName },
            ExcludeCredentials = [.. existing.Select(id => new PublicKeyCredentialDescriptor(id))],
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        return await StoreAsync(Register, userId, created.ToJson(), ct);
    }

    public async Task<PasskeyInfo?> CompleteRegistrationAsync(Guid userId, Guid challengeId, string name, string attestationJson, CancellationToken ct = default)
    {
        var challenge = await ConsumeAsync(challengeId, Register, ct);
        if (challenge is null || challenge.UserId != userId)
            return null;

        try
        {
            var attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationJson);
            if (attestation is null)
                return null;

            var registered = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestation,
                OriginalOptions = CredentialCreateOptions.FromJson(challenge.Options),
                IsCredentialIdUniqueToUserCallback = async (args, token) =>
                    !await db.Set<ModulusPasskey>().AnyAsync(p => p.CredentialId == args.CredentialId, token),
            }, ct);

            var passkey = new ModulusPasskey
            {
                Id = GuidV7.Create(),
                UserId = userId,
                CredentialId = registered.Id,
                PublicKey = registered.PublicKey,
                UserHandle = userId.ToByteArray(),
                SignCount = registered.SignCount,
                Name = string.IsNullOrWhiteSpace(name) ? "Passkey" : name.Trim()[..Math.Min(name.Trim().Length, 128)],
                CreatedAt = _clock.GetUtcNow(),
                AaGuid = registered.AaGuid,
                BackupEligible = registered.IsBackupEligible,
                BackedUp = registered.IsBackedUp,
                Transports = registered.Transports is { Length: > 0 } t ? string.Join(',', t.Select(x => x.ToString())) : null,
            };
            db.Set<ModulusPasskey>().Add(passkey);
            await db.SaveChangesAsync(ct);
            Record("passkey.registered", SecurityAuditOutcomes.Success, userId);
            return ToInfo(passkey);
        }
        catch (Exception ex) when (ex is Fido2VerificationException or JsonException or DbUpdateException)
        {
            Record("passkey.registered", SecurityAuditOutcomes.Denied, userId);
            return null;
        }
    }

    public async Task<PasskeyCeremony> BeginLoginAsync(CancellationToken ct = default)
    {
        // No allowed credentials: the authenticator offers the discoverable credentials for this relying party.
        var assertion = fido2.GetAssertionOptions(new GetAssertionOptionsParams { UserVerification = UserVerificationRequirement.Required });
        return await StoreAsync(Login, null, assertion.ToJson(), ct);
    }

    public async Task<PasswordGrantResult> ValidateLoginAsync(Guid challengeId, string assertionJson, CancellationToken ct = default)
    {
        var challenge = await ConsumeAsync(challengeId, Login, ct);
        if (challenge is null)
            return Deny(null, "unknown-challenge");

        AuthenticatorAssertionRawResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionJson);
        }
        catch (JsonException)
        {
            return Deny(null, "malformed-assertion");
        }

        if (response?.RawId is not { Length: > 0 } credentialId)
            return Deny(null, "malformed-assertion");

        var passkey = await db.Set<ModulusPasskey>().SingleOrDefaultAsync(p => p.CredentialId == credentialId, ct);
        if (passkey is null)
            return Deny(null, "unknown-credential");

        var user = await userManager.FindByIdAsync(passkey.UserId.ToString());
        if (user is not { IsActive: true } || await userManager.IsLockedOutAsync(user) || !await CanSignInAsync(user))
            return Deny(passkey.UserId, "account-unavailable");

        VerifyAssertionResult verified;
        try
        {
            verified = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = AssertionOptions.FromJson(challenge.Options),
                StoredPublicKey = passkey.PublicKey,
                StoredSignatureCounter = (uint)Math.Clamp(passkey.SignCount, 0, uint.MaxValue),
                IsUserHandleOwnerOfCredentialIdCallback = (args, token) => Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(passkey.UserHandle)),
            }, ct);
        }
        catch (Fido2VerificationException)
        {
            // Includes a signature counter that did not grow: a cloned authenticator.
            return Deny(passkey.UserId, "verification-failed");
        }

        passkey.SignCount = verified.SignCount;
        passkey.BackedUp = verified.IsBackedUp;
        passkey.LastUsedAt = _clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        Record("passkey.login", SecurityAuditOutcomes.Success, user.Id, user.TenantId);
        return new PasswordGrantResult
        {
            Success = true,
            Subject = await userManager.GetUserIdAsync(user),
            UserName = await userManager.GetUserNameAsync(user),
            Email = await userManager.GetEmailAsync(user),
            TenantId = user.TenantId,
            Roles = [.. await userManager.GetRolesAsync(user)],
            SecurityStamp = await userManager.GetSecurityStampAsync(user),
            // User verification is required, so a passkey is possession plus biometric or PIN: several factors in one gesture.
            AuthenticationMethods = [passkey.BackedUp ? "swk" : "hwk", "user", "mfa"],
        };
    }

    public async Task<IReadOnlyList<PasskeyInfo>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.Set<ModulusPasskey>().AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct);
        return [.. rows.OrderBy(p => p.CreatedAt).Select(ToInfo)];
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid passkeyId, CancellationToken ct = default)
    {
        var removed = await db.Set<ModulusPasskey>().Where(p => p.Id == passkeyId && p.UserId == userId).ExecuteDeleteAsync(ct) > 0;
        if (removed)
            Record("passkey.removed", SecurityAuditOutcomes.Success, userId);
        return removed;
    }

    private async Task<PasskeyCeremony> StoreAsync(string purpose, Guid? userId, string optionsJson, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        // Expired ceremonies are swept as new ones start, so the table stays small without a background job.
        await db.Set<ModulusPasskeyChallenge>().Where(c => c.ExpiresAt < now).ExecuteDeleteAsync(ct);

        var row = new ModulusPasskeyChallenge
        {
            Id = GuidV7.Create(),
            Purpose = purpose,
            UserId = userId,
            Options = optionsJson,
            ExpiresAt = now.AddSeconds(Settings.ChallengeLifetimeSeconds),
        };
        db.Set<ModulusPasskeyChallenge>().Add(row);
        await db.SaveChangesAsync(ct);
        db.Entry(row).State = EntityState.Detached;
        using var document = JsonDocument.Parse(optionsJson);
        return new PasskeyCeremony(row.Id, document.RootElement.Clone());
    }

    /// <summary>Reads a ceremony and deletes it in one conditional statement; null when it is unknown, expired, of another kind or already used.</summary>
    private async Task<ModulusPasskeyChallenge?> ConsumeAsync(Guid id, string purpose, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var row = await db.Set<ModulusPasskeyChallenge>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.Purpose == purpose && c.ExpiresAt >= now, ct);
        if (row is null)
            return null;

        return await db.Set<ModulusPasskeyChallenge>().Where(c => c.Id == id).ExecuteDeleteAsync(ct) == 1 ? row : null;
    }

    private async Task<bool> CanSignInAsync(TUser user)
    {
        var signIn = userManager.Options.SignIn;
        return !signIn.RequireConfirmedEmail || await userManager.IsEmailConfirmedAsync(user);
    }

    private PasswordGrantResult Deny(Guid? userId, string reason)
    {
        Record("passkey.login", SecurityAuditOutcomes.Denied, userId, reason: reason);
        return PasswordGrantResult.Denied();
    }

    private void Record(string action, string outcome, Guid? userId, Guid? tenantId = null, string? reason = null)
        => audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Outcome = outcome,
            TenantId = tenantId,
            Target = userId is { } id ? $"user:{id}" + (reason is null ? "" : $" ({reason})") : reason,
        });

    private static PasskeyInfo ToInfo(ModulusPasskey p) => new(p.Id, p.Name, p.CreatedAt, p.LastUsedAt, p.BackedUp);
}
