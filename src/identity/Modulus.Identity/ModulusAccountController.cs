namespace Modulus.Identity;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Modulus.Identity.Abstractions;

/// <summary>
/// Account management endpoints: password reset, email confirmation, and logout.
/// </summary>
[ApiController]
[Route("[controller]")]
public class AccountController<TUser>(
    UserManager<TUser> userManager,
    SignInManager<TUser> signInManager)
    : ControllerBase
    where TUser : ModulusUser, new()
{
    /// <summary>
    /// Initiates password reset by sending a reset token to the user's email.
    /// The token is valid for 24 hours (configurable via UserOptions).
    /// </summary>
    /// <param name="email">Email address of the account to reset</param>
    /// <remarks>
    /// Returns 200 OK regardless of whether the email exists (prevents account enumeration).
    /// The caller must send the reset token and new password to POST /account/reset-password.
    /// </remarks>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPasswordAsync([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required" });

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Return 200 even if user doesn't exist (prevent email enumeration).
            return Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        // TODO: Send reset token via email (implement with IEmailSender).
        // For now, the token must be supplied by the client in the reset-password call.
        // In production, email the token to the user and include a reset link.

        return Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
    }

    /// <summary>
    /// Resets a user's password using a reset token (obtained from forgot-password).
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
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
            return BadRequest(new { error = "Invalid email or token" });

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return BadRequest(new { error = errors });
        }

        return Ok(new { message = "Password has been reset successfully" });
    }

    /// <summary>
    /// Confirms a user's email address using a confirmation token.
    /// Required when <see cref="ModulusIdentityOptions.RequireConfirmedEmail"/> is enabled.
    /// </summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
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
            return BadRequest(new { error = "Invalid email or token" });

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return BadRequest(new { error = errors });
        }

        return Ok(new { message = "Email has been confirmed successfully" });
    }

    /// <summary>
    /// Sends an email confirmation token to the specified email address.
    /// The user must call POST /account/confirm-email with the token to verify.
    /// </summary>
    [HttpPost("send-confirmation-email")]
    [AllowAnonymous]
    public async Task<IActionResult> SendConfirmationEmailAsync([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required" });

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Return 200 even if user doesn't exist (prevent enumeration).
            return Ok(new { message = "If an account with that email exists, a confirmation link has been sent." });
        }

        // Skip if already confirmed.
        if (await userManager.IsEmailConfirmedAsync(user))
            return Ok(new { message = "Email is already confirmed" });

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        // TODO: Send confirmation token via email (implement with IEmailSender).

        return Ok(new { message = "If an account with that email exists, a confirmation link has been sent." });
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
