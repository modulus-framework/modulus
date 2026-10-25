namespace Modulus.Identity;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;
using Modulus.Storage;

/// <summary>
/// Account management endpoints: password reset, email confirmation, and logout.
/// The controller is generic over the user type registered by
/// <c>AddModulusIdentity&lt;TContext, TUser, TRole&gt;</c>; MVC discovers the
/// closed generic via <see cref="AccountControllerFeatureProvider"/> (the
/// default feature provider rejects generic controllers, which would leave
/// every route here unreachable).
/// </summary>
/// <remarks>
/// Tokens are delivered through <see cref="IIdentityEmailSender"/> — the
/// framework never returns reset/confirmation tokens in API responses (that
/// would let anyone reset any account by first calling forgot-password).
/// Wire a real sender; the default no-op discards the email, which keeps the
/// flows unusable (fail-closed) rather than insecure.
/// </remarks>
[ApiController]
[Route("account")]
[HostTenantContext]
public class AccountController<TUser>(
    UserManager<TUser> userManager,
    SignInManager<TUser> signInManager,
    IIdentityEmailQueue emailQueue,
    IUserSessionService sessions,
    IUserTwoFactorService twoFactor,
    ILoginHistoryService loginHistory)
    : ControllerBase
    where TUser : ModulusUser, new()
{
    // Uniform response for "email may or may not exist" flows — returning a
    // different shape for known vs unknown emails would enumerate accounts.
    private const string NonCommittalMessage =
        "If an account with that email exists, a link has been sent.";

    /// <summary>
    /// Initiates password reset by sending a reset token to the user's email.
    /// The token is valid for 24 hours (configurable via UserOptions).
    /// </summary>
    /// <param name="email">Email address of the account to reset</param>
    /// <remarks>
    /// Returns 200 OK regardless of whether the email exists (prevents account enumeration).
    /// The caller must send the reset token and new password to POST /account/reset-password.
    /// Anonymous token-email dispatch: each call mints a token and an email, so
    /// this endpoint relies on the global rate limiter
    /// (<c>UseModulusRateLimiting</c> — anonymous callers fall back to the
    /// per-IP bucket) to bound mail-bombing/token-flooding. Do not expose it
    /// without the limiter wired.
    /// </remarks>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [Loosened("Password reset request: the user cannot sign in", Framework = true)]
    public async Task<IActionResult> ForgotPasswordAsync([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required" });

        // Look-up only: the token and the mail are produced off the request, so a known and an unknown address take
        // the same time to answer.
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null && user.MayStartNewAccess())
            emailQueue.EnqueuePasswordReset(user.Id);

        return Ok(new { message = NonCommittalMessage });
    }

    /// <summary>
    /// Resets a user's password using a reset token (obtained from forgot-password).
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [Loosened("Password reset with an emailed token: the user cannot sign in", Framework = true)]
    public async Task<IActionResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Email, token, and new password are required" });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return InvalidEmailOrToken();

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            return MapIdentityFailure(result);

        return Ok(new { message = "Password has been reset successfully" });
    }

    /// <summary>
    /// Accepts an invitation: sets the first password with the token from the invitation mail and confirms the address (the token
    /// proves the mailbox). Same answers as password reset, so unknown and invalid look alike.
    /// </summary>
    [HttpPost("accept-invitation")]
    [AllowAnonymous]
    [Loosened("Accepting an invitation with the emailed token: the invited user cannot sign in yet", Framework = true)]
    public async Task<IActionResult> AcceptInvitationAsync([FromBody] AcceptInvitationRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { error = "Email, token, and new password are required" });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || await userManager.HasPasswordAsync(user))
            return InvalidEmailOrToken();

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            return MapIdentityFailure(result);

        // Using the invitation activates the account: it may sign in from now on.
        var activating = user.Lifecycle == AccountLifecycle.PendingActivation;
        if (activating)
            user.Lifecycle = AccountLifecycle.Active;
        if (!user.EmailConfirmed || activating)
        {
            user.EmailConfirmed = true;
            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded)
                return MapIdentityFailure(updated);
        }

        return Ok(new { message = "Invitation accepted. You can sign in." });
    }

    /// <summary>
    /// Confirms a user's email address using a confirmation token.
    /// Required when <see cref="ModulusIdentityOptions.RequireConfirmedEmail"/> is enabled.
    /// </summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [Loosened("Email confirmation link from the confirmation email", Framework = true)]
    public async Task<IActionResult> ConfirmEmailAsync(
        [FromBody] ConfirmEmailRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new { error = "Email and token are required" });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return InvalidEmailOrToken();

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return MapIdentityFailure(result);

        return Ok(new { message = "Email has been confirmed successfully" });
    }

    /// <summary>
    /// Sends an email confirmation token to the specified email address.
    /// The user must call POST /account/confirm-email with the token to verify.
    /// Anonymous token-email dispatch — see
    /// <see cref="ForgotPasswordAsync"/>: keep <c>UseModulusRateLimiting</c>
    /// wired so the per-IP bucket bounds mail-bombing.
    /// </summary>
    [HttpPost("send-confirmation-email")]
    [AllowAnonymous]
    [Loosened("Resends the confirmation email before the first sign-in", Framework = true)]
    public async Task<IActionResult> SendConfirmationEmailAsync([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required" });

        var user = await userManager.FindByEmailAsync(email);
        if (user is not null && user.MayStartNewAccess())
            emailQueue.EnqueueEmailConfirmation(user.Id);

        return Ok(new { message = NonCommittalMessage });
    }

    /// <summary>
    /// Signs the user out and revokes their session.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> LogoutAsync()
    {
        await signInManager.SignOutAsync();
        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>The caller's active sessions (issued tokens): where they are signed in.</summary>
    [HttpGet("sessions")]
    [Authorize]
    public async Task<IActionResult> ListSessionsAsync(CancellationToken ct)
        => CallerId() is { } id ? Ok(await sessions.ListAsync(id, ct)) : Unauthorized();

    /// <summary>Ends one of the caller's sessions.</summary>
    [HttpDelete("sessions/{sessionId}")]
    [Authorize]
    public async Task<IActionResult> RevokeSessionAsync(string sessionId, CancellationToken ct)
    {
        if (CallerId() is not { } id)
            return Unauthorized();

        return await sessions.RevokeAsync(id, sessionId, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// The caller's sign-ins (with <c>Identity:Sessions</c> on): where from, which browser or device, when last used. The one the
    /// request itself belongs to is marked <c>current</c>.
    /// </summary>
    [HttpGet("sign-ins")]
    [Authorize]
    public async Task<IActionResult> ListSignInsAsync(CancellationToken ct)
    {
        if (CallerId() is not { } id)
            return Unauthorized();
        if (HttpContext.RequestServices.GetService<ISessionRegistry>() is not { } registry)
            return NotFound();

        var current = User.FindFirst("sid")?.Value;
        return Ok((await registry.ListAsync(id, ct)).Select(s => new
        {
            s.Id,
            s.ClientId,
            s.DeviceName,
            s.UserAgent,
            s.IpAddress,
            s.Methods,
            s.CreatedAt,
            s.LastSeenAt,
            Current = string.Equals(s.Id.ToString(), current, StringComparison.OrdinalIgnoreCase),
        }));
    }

    /// <summary>Ends one of the caller's sign-ins; the tokens of that session stop working at once.</summary>
    [HttpDelete("sign-ins/{sessionId:guid}")]
    [Authorize]
    public async Task<IActionResult> EndSignInAsync(Guid sessionId, CancellationToken ct)
    {
        if (CallerId() is not { } id)
            return Unauthorized();
        if (HttpContext.RequestServices.GetService<ISessionRegistry>() is not { } registry)
            return NotFound();

        return await registry.EndAsync(id, sessionId, "revoked", ct) ? NoContent() : NotFound();
    }

    /// <summary>Ends every session of the caller, this one included (sign out everywhere).</summary>
    [HttpPost("sessions/revoke-all")]
    [Authorize]
    public async Task<IActionResult> RevokeAllSessionsAsync(CancellationToken ct)
    {
        if (CallerId() is not { } id)
            return Unauthorized();

        await sessions.RevokeAllAsync(id, "signed out everywhere by the account holder", ct);
        await signInManager.SignOutAsync();
        return NoContent();
    }

    /// <summary>The caller's recent sign-ins and refused attempts, newest first (<c>?take=</c>, default 50).</summary>
    [HttpGet("login-history")]
    [Authorize]
    public async Task<IActionResult> LoginHistoryAsync([FromQuery] int take = 50, CancellationToken ct = default)
        => CallerId() is { } id ? Ok(await loginHistory.GetAsync(id, take, ct)) : Unauthorized();

    /// <summary>Starts adding an authenticator app: returns the key and the <c>otpauth://</c> URI (a QR code). Enforced only after <c>2fa/enable</c>.</summary>
    [HttpPost("2fa/setup")]
    [Authorize]
    public async Task<IActionResult> SetUpTwoFactorAsync(CancellationToken ct)
    {
        if (CallerId() is not { } id)
            return Unauthorized();

        // Replacing the key of an enabled account would let a stolen session take over the second factor: the owner
        // disables it first, which needs the password.
        if (await twoFactor.IsEnabledAsync(id, ct))
            return Conflict(new { error = "Two-factor is already on. Turn it off before setting up a new authenticator app." });

        return await twoFactor.BeginSetupAsync(id, ct) is { } setup ? Ok(setup) : Unauthorized();
    }

    /// <summary>Confirms the authenticator app with a current code, turns two-factor on and returns the recovery codes (shown once).</summary>
    [HttpPost("2fa/enable")]
    [Authorize]
    public async Task<IActionResult> EnableTwoFactorAsync([FromBody] TwoFactorCodeRequest request, CancellationToken ct)
    {
        if (CallerId() is not { } id)
            return Unauthorized();

        return await twoFactor.EnableAsync(id, request.Code ?? "", ct) is { } codes
            ? Ok(new { recoveryCodes = codes })
            : BadRequest(new { error = "The code is not valid." });
    }

    /// <summary>Turns two-factor off. Needs the account's password (a sensitive change), and ends the other sessions.</summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    public async Task<IActionResult> DisableTwoFactorAsync([FromBody] TwoFactorDisableRequest request, CancellationToken ct)
    {
        if (CallerId() is not { } id || await userManager.FindByIdAsync(id.ToString()) is not { } user)
            return Unauthorized();
        // CheckPasswordSignInAsync counts a wrong password toward lock-out (CheckPasswordAsync does not) and refuses a locked account.
        if (string.IsNullOrEmpty(request.Password) ||
            !(await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            return BadRequest(new { error = "The password is not correct." });

        await twoFactor.DisableAsync(id, ct);
        return NoContent();
    }

    /// <summary>The caller's own profile: name, language, time zone, phone and the address on the account.</summary>
    [HttpGet("profile")]
    [HttpGet("/api/account/profile")]
    [Authorize]
    public async Task<IActionResult> GetProfileAsync()
    {
        var user = await CurrentUserAsync();
        return user is null ? Unauthorized() : Ok(ProfileView(user));
    }

    /// <summary>
    /// The caller's profile details (premodule 3): names, photo address, work e-mail, job title and department, with the employee code and
    /// primary branch an administrator manages (read-only here), and the system-managed fields. Additive to <c>account/profile</c>.
    /// </summary>
    [HttpGet("profile/details")]
    [HttpGet("/api/account/profile/details")]
    [Authorize]
    public async Task<IActionResult> GetProfileDetailsAsync()
    {
        var user = await CurrentUserAsync();
        return user is null ? Unauthorized() : Ok(DetailsView(user));
    }

    /// <summary>
    /// Updates the caller's profile details. A field left out (null) keeps its value; an empty string clears it. The employee code, the
    /// primary branch, the sign-in address, the account state and the timestamps are not in this request, so the person cannot set them.
    /// </summary>
    [HttpPatch("profile/details")]
    [HttpPatch("/api/account/profile/details")]
    [Authorize]
    public async Task<IActionResult> UpdateProfileDetailsAsync([FromBody] UpdateProfileDetailsRequest request, [FromServices] IIdentityEmailSender emailSender, CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();
        if (!ProfileRules.TryNormalizeDetails(request.FirstName, request.LastName, request.AvatarUrl, request.WorkEmail, request.JobTitle, request.Department,
                out var values, out var errors))
            return BadRequest(new { errors });

        if (request.FirstName is not null) user.FirstName = values.FirstName;
        if (request.LastName is not null) user.LastName = values.LastName;
        if (request.AvatarUrl is not null) user.AvatarUrl = values.AvatarUrl;
        if (request.JobTitle is not null) user.JobTitle = values.JobTitle;
        if (request.Department is not null) user.Department = values.Department;
        // A work e-mail is never set directly: an address starts a confirmation (below), and an empty value withdraws it at once.
        if (request.WorkEmail is not null && values.WorkEmail is null)
        {
            user.WorkEmail = null;
            user.PendingWorkEmail = null;
            user.WorkEmailVerificationStamp = Guid.NewGuid().ToString("N");
        }

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return MapIdentityFailure(result);
        if (request.WorkEmail is not null && values.WorkEmail is { } address && await RequestWorkEmailAsync(user, address, emailSender, ct) is { } refused)
            return refused;
        return Ok(DetailsView(user));
    }

    /// <summary>
    /// Starts the confirmation of a work e-mail address and mails the link to it. The answer does not depend on the account, so it does
    /// not show whether the address is in use. Returns the refusal, or <c>null</c> when the link was sent (or nothing was needed).
    /// </summary>
    private async Task<IActionResult?> RequestWorkEmailAsync(TUser user, string address, IIdentityEmailSender emailSender, CancellationToken ct)
    {
        var verifier = HttpContext.RequestServices.GetRequiredService<WorkEmailVerifier<TUser>>();
        var request = await verifier.RequestAsync(user, address);
        switch (request.Outcome)
        {
            case WorkEmailRequestOutcome.Sent:
                await emailSender.SendWorkEmailVerificationAsync(address, request.Token!, ct);
                AuditWorkEmail("identity.user.work-email.requested", user);
                return null;
            case WorkEmailRequestOutcome.TooSoon:
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "WORK_EMAIL_TOO_SOON", message = "Wait a minute before asking for another confirmation link." });
            case WorkEmailRequestOutcome.Failed:
                return MapIdentityFailure(request.Result!);
            default:
                return null;
        }
    }

    /// <summary>
    /// Confirms the caller's pending work e-mail with the link's token. The link works once, for 30 minutes by default
    /// (<c>Identity:WorkEmail</c>), and only the newest one works; the sign-in e-mail is not changed.
    /// </summary>
    [HttpPost("profile/work-email/confirm")]
    [HttpPost("/api/account/profile/work-email/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmWorkEmailAsync([FromBody] ConfirmWorkEmailRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();
        if (string.IsNullOrEmpty(request.Token))
            return InvalidWorkEmailLink();

        var verifier = HttpContext.RequestServices.GetRequiredService<WorkEmailVerifier<TUser>>();
        switch (await verifier.ConfirmAsync(user.Id, request.Token))
        {
            case WorkEmailConfirmOutcome.Verified:
                AuditWorkEmail("identity.user.work-email.verified", user);
                return Ok(DetailsView(user));
            case WorkEmailConfirmOutcome.Failed:
                return Conflict(new { error = "WORK_EMAIL_CHANGED", message = "The account changed while the link was used. Try the link again." });
            case WorkEmailConfirmOutcome.TooManyAttempts:
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "WORK_EMAIL_TOO_MANY_ATTEMPTS", message = "Too many attempts. Ask for a new confirmation link." });
            case WorkEmailConfirmOutcome.Expired:
                return StatusCode(StatusCodes.Status410Gone, new { error = "WORK_EMAIL_LINK_EXPIRED", message = "This confirmation link has expired. Ask for a new one." });
            case WorkEmailConfirmOutcome.Used:
                return StatusCode(StatusCodes.Status410Gone, new { error = "WORK_EMAIL_LINK_USED", message = "This address is already confirmed. The link cannot be used again." });
            case WorkEmailConfirmOutcome.Superseded:
                return BadRequest(new { error = "WORK_EMAIL_LINK_SUPERSEDED", message = "A newer confirmation link was sent. Use the newest link, or ask for a new one." });
            default:
                return InvalidWorkEmailLink();
        }
    }

    private IActionResult InvalidWorkEmailLink()
        => BadRequest(new { error = "WORK_EMAIL_LINK_INVALID", message = "The confirmation link is invalid, expired or already used. Ask for a new one." });

    private void AuditWorkEmail(string action, TUser user)
        => HttpContext.RequestServices.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Actor = user.Id.ToString("D"),
            Target = $"user:{user.Id:D}",
        });

    /// <summary>
    /// Replaces the caller's photo (premodule 3). The body is the image; its bytes decide the format (JPEG, PNG or WebP, still, at most
    /// 5 MB, sides up to 4096 px), not its content type. The stored photo is a normalized square under a random key, and the original is
    /// not kept. The previous photo is removed only after the new one is saved; if saving the account fails, the new one is removed.
    /// </summary>
    [HttpPut("profile/photo")]
    [HttpPut("/api/account/profile/photo")]
    [Authorize]
    public async Task<IActionResult> UploadProfilePhotoAsync(CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();
        var storage = HttpContext.RequestServices.GetService<IFileStorage>();
        if (storage is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "PHOTO_STORAGE_UNAVAILABLE", message = "Photo storage is not configured." });

        var processor = HttpContext.RequestServices.GetRequiredService<IProfilePhotoProcessor>();
        var processed = await processor.ProcessAsync(Request.Body, ct);
        if (!processed.Accepted)
            return PhotoRefused(processed.Refusal);

        var assetId = Guid.NewGuid();
        var path = ProfilePhotoRules.StoragePath(user.TenantId, assetId);
        await using (var stream = new MemoryStream(processed.Jpeg!))
            await storage.UploadAsync(path, stream, "image/jpeg", ct);

        var previous = user.PhotoAssetId;
        user.PhotoAssetId = assetId;
        IdentityResult result;
        try
        {
            result = await userManager.UpdateAsync(user);
        }
        catch
        {
            await DeleteStoredPhotoAsync(storage, path, ct);
            throw;
        }
        if (!result.Succeeded)
        {
            await DeleteStoredPhotoAsync(storage, path, ct);
            return MapIdentityFailure(result);
        }

        if (previous is { } replaced)
            await DeleteStoredPhotoAsync(storage, ProfilePhotoRules.StoragePath(user.TenantId, replaced), ct);
        return Ok(DetailsView(user));
    }

    /// <summary>Removes the caller's photo. The account is saved first, so a stored file is removed only once nothing refers to it.</summary>
    [HttpDelete("profile/photo")]
    [HttpDelete("/api/account/profile/photo")]
    [Authorize]
    public async Task<IActionResult> DeleteProfilePhotoAsync(CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();
        if (user.PhotoAssetId is not { } current)
            return NoContent();

        user.PhotoAssetId = null;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return MapIdentityFailure(result);

        if (HttpContext.RequestServices.GetService<IFileStorage>() is { } storage)
            await DeleteStoredPhotoAsync(storage, ProfilePhotoRules.StoragePath(user.TenantId, current), ct);
        return NoContent();
    }

    /// <summary>The caller's photo, as a JPEG. Only the account's own photo is served, and only from the account's own tenant folder.</summary>
    [HttpGet("profile/photo")]
    [HttpGet("/api/account/profile/photo")]
    [Authorize]
    public async Task<IActionResult> GetProfilePhotoAsync(CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();
        if (user.PhotoAssetId is not { } current || HttpContext.RequestServices.GetService<IFileStorage>() is not { } storage)
            return NotFound();

        var path = ProfilePhotoRules.StoragePath(user.TenantId, current);
        if (!await storage.ExistsAsync(path, ct))
            return NotFound();

        Response.Headers.CacheControl = "private, no-cache";
        return File(await storage.DownloadAsync(path, ct), "image/jpeg");
    }

    private IActionResult PhotoRefused(ProfilePhotoRefusal refusal)
    {
        var (status, code, message) = ProfilePhotoRules.Describe(refusal);
        return StatusCode(status, new { error = code, message });
    }

    /// <summary>
    /// Removes a stored photo. A failure is logged, not thrown: the account no longer refers to the file, and a leftover is an orphan a
    /// cleanup can remove, so it should not fail the request that already changed the account.
    /// </summary>
    private async Task DeleteStoredPhotoAsync(IFileStorage storage, string path, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(path, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Modulus.Identity.ProfilePhotos")
                .LogWarning(ex, "Could not remove the profile photo {Path}; it is an orphan until a cleanup removes it.", path);
        }
    }

    /// <summary>
    /// Updates the caller's own name, language, time zone and phone. A person changes only their own profile, and the address
    /// changes only through <c>profile/email</c>.
    /// </summary>
    [HttpPut("profile")]
    [HttpPut("/api/account/profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfileAsync([FromBody] UpdateProfileRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();
        if (!ProfileRules.TryNormalize(request.DisplayName, request.Language, request.TimeZone, request.PhoneNumber,
                out var values, out var errors))
            return BadRequest(new { errors });

        user.DisplayName = values.DisplayName;
        user.Language = values.Language;
        user.TimeZone = values.TimeZone;
        if (!string.Equals(user.PhoneNumber, values.PhoneNumber, StringComparison.Ordinal))
        {
            // A changed number is unconfirmed until the person proves it again.
            user.PhoneNumber = values.PhoneNumber;
            user.PhoneNumberConfirmed = false;
        }

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? Ok(ProfileView(user)) : MapIdentityFailure(result);
    }

    /// <summary>
    /// Starts an email change: a one-time link goes to the <em>new</em> address, and the change completes only when that link is used
    /// (<c>profile/email/confirm</c>). The answer is the same whether or not the address is taken, so this does not show which addresses
    /// have accounts. The mail is sent inline (not through the queue, which only mails stored addresses), so the caller is signed in.
    /// </summary>
    [HttpPost("profile/email")]
    [HttpPost("/api/account/profile/email")]
    [Authorize]
    public async Task<IActionResult> RequestEmailChangeAsync(
        [FromBody] ChangeEmailRequest request,
        [FromServices] IIdentityEmailSender emailSender,
        CancellationToken ct)
    {
        var user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();
        var address = ProfileRules.NormalizeEmail(request.NewEmail);
        if (address is null)
            return BadRequest(new { errors = new Dictionary<string, string[]> { ["newEmail"] = ["Enter a valid email address."] } });

        if (!string.Equals(address, user.Email, StringComparison.OrdinalIgnoreCase) && await userManager.FindByEmailAsync(address) is null)
        {
            var token = await userManager.GenerateChangeEmailTokenAsync(user, address);
            await emailSender.SendEmailChangeEmailAsync(address, token, ct);
        }

        return Accepted(new { message = "If the address can be used, a link has been sent to it." });
    }

    /// <summary>Completes an email change with the link's token. The token works only for this account and this address.</summary>
    [HttpPost("profile/email/confirm")]
    [HttpPost("/api/account/profile/email/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmEmailChangeAsync([FromBody] ConfirmEmailChangeRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null)
            return Unauthorized();
        var address = ProfileRules.NormalizeEmail(request.NewEmail);
        if (address is null || string.IsNullOrEmpty(request.Token) || await userManager.FindByEmailAsync(address) is not null)
            return InvalidEmailOrToken();

        var result = await userManager.ChangeEmailAsync(user, address, request.Token);
        return result.Succeeded ? Ok(ProfileView(user)) : MapIdentityFailure(result);
    }

    private async Task<TUser?> CurrentUserAsync()
        => CallerId() is { } id ? await userManager.FindByIdAsync(id.ToString()) : null;

    private static ProfileDetailsResponse DetailsView(TUser user) => new(
        user.Id, user.UserName, user.Email, user.IsActive, user.FirstName, user.LastName, user.AvatarUrl, user.WorkEmail, user.JobTitle,
        user.Department, user.EmployeeCode, user.PrimaryBranchId, user.CreatedAt, user.ModifiedAt, user.PhotoAssetId is not null, user.PendingWorkEmail, user.PhotoAssetId is not null ? "/api/account/profile/photo" : null);

    private static ProfileResponse ProfileView(TUser user) => new(
        user.DisplayName, user.Language, user.TimeZone, user.PhoneNumber, user.Email, user.EmailConfirmed);

    private Guid? CallerId()
        => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value, out var id) ? id : null;

    /// <summary>
    /// Same body for "unknown email" and "invalid token" so the two cases are
    /// indistinguishable to callers (anti-enumeration).
    /// </summary>
    private static BadRequestObjectResult InvalidEmailOrToken()
        => new(new { error = "Invalid email or token" });

    /// <summary>
    /// Maps an <see cref="IdentityResult"/> failure without leaking which
    /// part was wrong for token-shaped failures: a bad/expired token returns
    /// the same generic error as an unknown email, while password-policy
    /// errors (useful only to a legitimate caller holding a valid token) are
    /// surfaced verbatim.
    /// </summary>
    private static BadRequestObjectResult MapIdentityFailure(IdentityResult result)
    {
        if (result.Errors.Any(e =>
                string.Equals(e.Code, "InvalidToken", StringComparison.Ordinal)))
        {
            return InvalidEmailOrToken();
        }

        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
        return new BadRequestObjectResult(new { error = errors });
    }
}

/// <summary>
/// Request body for password reset.
/// </summary>
public sealed class ResetPasswordRequest
{
    public required string Email { get; set; }
    public required string Token { get; set; }
    public required string NewPassword { get; set; }
}

/// <summary>
/// Request body for email confirmation.
/// </summary>
public sealed class ConfirmEmailRequest
{
    public required string Email { get; set; }
    public required string Token { get; set; }
}

/// <summary>Request body for <c>2fa/enable</c>.</summary>
public sealed class TwoFactorCodeRequest
{
    /// <summary>The current code from the authenticator app.</summary>
    public string? Code { get; set; }
}

/// <summary>Request body for <c>2fa/disable</c>.</summary>
public sealed class TwoFactorDisableRequest
{
    /// <summary>The account's password.</summary>
    public string? Password { get; set; }
}

/// <summary>Request body for <c>PUT account/profile</c>. Every field is optional; an empty value clears it.</summary>
public sealed class UpdateProfileRequest
{
    /// <summary>The name the person shows to others.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The language as a BCP 47 tag, for example <c>en-GB</c>.</summary>
    public string? Language { get; set; }

    /// <summary>The time zone as an IANA or Windows id, for example <c>Europe/London</c>.</summary>
    public string? TimeZone { get; set; }

    /// <summary>The contact phone number.</summary>
    public string? PhoneNumber { get; set; }
}

/// <summary>The caller's profile (<c>GET account/profile</c>).</summary>
/// <param name="DisplayName">The chosen name, if any.</param>
/// <param name="Language">The BCP 47 language tag, if any.</param>
/// <param name="TimeZone">The time zone id, if any.</param>
/// <param name="PhoneNumber">The contact phone number, if any.</param>
/// <param name="Email">The address on the account.</param>
/// <param name="EmailConfirmed">Whether the address has been confirmed.</param>
public sealed record ProfileResponse(
    string? DisplayName,
    string? Language,
    string? TimeZone,
    string? PhoneNumber,
    string? Email,
    bool EmailConfirmed);

/// <summary>
/// Request body for <c>PATCH account/profile/details</c>. A field left out (null) keeps its value; an empty string clears it.
/// Employee code, primary branch and the account's own address are not here: an administrator sets the first two, and the address changes only through <c>profile/email</c>.
/// </summary>
public sealed class UpdateProfileDetailsRequest
{
    /// <summary>The first name (at most 100 characters).</summary>
    public string? FirstName { get; set; }

    /// <summary>The last name (at most 100 characters).</summary>
    public string? LastName { get; set; }

    /// <summary>The address of the profile photo: an https address of at most 2048 characters.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>The work e-mail address, a contact address (the sign-in address is separate).</summary>
    public string? WorkEmail { get; set; }

    /// <summary>The job title (at most 100 characters).</summary>
    public string? JobTitle { get; set; }

    /// <summary>The department (at most 100 characters).</summary>
    public string? Department { get; set; }
}

/// <summary>
/// The caller's profile details (<c>GET account/profile/details</c>). The fields an administrator manages (<see cref="EmployeeCode"/>,
/// <see cref="PrimaryBranchId"/>) and the system-managed ones (<see cref="Id"/>, <see cref="UserName"/>, <see cref="IsActive"/>,
/// <see cref="CreatedAt"/>, <see cref="ModifiedAt"/>) are read-only here.
/// </summary>
public sealed record ProfileDetailsResponse(
    Guid Id,
    string? UserName,
    string? Email,
    bool IsActive,
    string? FirstName,
    string? LastName,
    string? AvatarUrl,
    string? WorkEmail,
    string? JobTitle,
    string? Department,
    string? EmployeeCode,
    Guid? PrimaryBranchId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    bool HasPhoto = false,
    string? PendingWorkEmail = null,
    string? PhotoUrl = null);

/// <summary>Request body for <c>profile/work-email/confirm</c>: the token from the confirmation link.</summary>
public sealed class ConfirmWorkEmailRequest
{
    public string? Token { get; set; }
}

/// <summary>Request body for <c>profile/email</c>: the address the account should move to.</summary>
public sealed class ChangeEmailRequest
{
    /// <summary>The new address.</summary>
    public string? NewEmail { get; set; }
}

/// <summary>Request body for <c>profile/email/confirm</c>.</summary>
public sealed class ConfirmEmailChangeRequest
{
    /// <summary>The address the link was sent to.</summary>
    public string? NewEmail { get; set; }

    /// <summary>The token from the link.</summary>
    public string? Token { get; set; }
}

/// <summary>Request body for <c>accept-invitation</c>.</summary>
public sealed class AcceptInvitationRequest
{
    /// <summary>The invited e-mail address.</summary>
    public required string Email { get; set; }

    /// <summary>The token from the invitation mail.</summary>
    public required string Token { get; set; }

    /// <summary>The password the new user chooses.</summary>
    public required string NewPassword { get; set; }
}
