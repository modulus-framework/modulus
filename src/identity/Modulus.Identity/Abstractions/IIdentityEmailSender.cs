namespace Modulus.Identity.Abstractions;

/// <summary>
/// Delivers identity emails (password-reset and email-confirmation links).
/// The account endpoints generate the tokens; this abstraction hands the
/// delivery mechanism to the app (SMTP, SendGrid, a message queue, …) so the
/// framework never needs a mail dependency.
/// </summary>
/// <remarks>
/// Implementations MUST NOT log the token itself — a leaked reset or
/// confirmation token is account takeover. Register a custom implementation
/// with <c>TryAddScoped&lt;IIdentityEmailSender&gt;</c> before
/// <c>AddModulusIdentity</c>; the default <see cref="NoopIdentityEmailSender"/>
/// discards the email (the flows then cannot complete until a real sender is
/// wired, which is fail-closed).
/// </remarks>
public interface IIdentityEmailSender
{
    /// <summary>
    /// Sends a password-reset email carrying <paramref name="resetToken"/>
    /// for the account with <paramref name="email"/>.
    /// </summary>
    Task SendPasswordResetEmailAsync(
        string email, string resetToken, CancellationToken ct = default);

    /// <summary>
    /// Sends an email-confirmation email carrying
    /// <paramref name="confirmationToken"/> for the account with
    /// <paramref name="email"/>.
    /// </summary>
    Task SendEmailConfirmationEmailAsync(
        string email, string confirmationToken, CancellationToken ct = default);

    /// <summary>
    /// Sends an invitation: the recipient was invited to the application and sets a first password with
    /// <paramref name="invitationToken"/> (<c>POST account/accept-invitation</c>). The default sends it as a password-reset mail,
    /// since the token works the same way; override it to word the mail as an invitation.
    /// </summary>
    Task SendInvitationEmailAsync(string email, string invitationToken, CancellationToken ct = default)
        => SendPasswordResetEmailAsync(email, invitationToken, ct);
}

/// <summary>
/// Default no-op sender: accepts the email and silently discards it. Register
/// a real sender to make the reset/confirmation flows usable.
/// </summary>
public sealed class NoopIdentityEmailSender : IIdentityEmailSender
{
    public Task SendPasswordResetEmailAsync(
        string email, string resetToken, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SendEmailConfirmationEmailAsync(
        string email, string confirmationToken, CancellationToken ct = default)
        => Task.CompletedTask;
}
