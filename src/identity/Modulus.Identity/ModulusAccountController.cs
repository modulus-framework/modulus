namespace Modulus.Identity;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Modulus.Identity.Abstractions;
using Modulus.Core.Abstractions.Security;

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
    IUserSessionService sessions)
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
        if (user is not null && user.IsActive)
            emailQueue.EnqueuePasswordReset(user.Id, email);

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
        if (user is not null && user.IsActive)
            emailQueue.EnqueueEmailConfirmation(user.Id, email);

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
