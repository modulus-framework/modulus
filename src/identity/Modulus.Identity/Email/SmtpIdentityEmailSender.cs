namespace Modulus.Identity.Email;

using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;

/// <summary>The part of mail sending the sender needs. The SMTP implementation is <see cref="SmtpIdentityEmailTransport"/>; tests supply their own.</summary>
public interface IIdentityEmailTransport
{
    Task SendAsync(MailMessage message, CancellationToken ct);
}

/// <summary>Sends a message through the SMTP server in <see cref="IdentityEmailOptions"/>.</summary>
public sealed class SmtpIdentityEmailTransport(IOptions<IdentityEmailOptions> options) : IIdentityEmailTransport
{
    public async Task SendAsync(MailMessage message, CancellationToken ct)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.SmtpHost!, settings.SmtpPort)
        {
            EnableSsl = settings.UseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 30_000,
        };
        if (settings.SmtpUserName is not null)
            client.Credentials = new NetworkCredential(settings.SmtpUserName, settings.SmtpPassword);
        await client.SendMailAsync(message, ct);
    }
}

/// <summary>
/// Sends identity mail with links built from <see cref="IdentityEmailOptions.BaseUrl"/>. Each link goes to the page that handles it:
/// <c>/ConfirmWorkEmail?token=</c> (work e-mail), <c>/ConfirmEmailChange?NewEmail=&amp;Token=</c> (address change), and the password-reset and
/// sign-up confirmation paths, which the application provides (the Web template does not include pages for them). The token is escaped into
/// the link and never logged.
/// </summary>
public sealed class SmtpIdentityEmailSender(IOptions<IdentityEmailOptions> options, IIdentityEmailTransport transport) : IIdentityEmailSender
{
    public const string WorkEmailPath = "/ConfirmWorkEmail";
    public const string EmailChangePath = "/ConfirmEmailChange";
    public const string PasswordResetPath = "/ResetPassword";
    public const string EmailConfirmationPath = "/ConfirmEmail";

    /// <summary>The base address, checked: absolute, https, or http on localhost only. Refuses to build a link that leaves the site unprotected.</summary>
    public static Uri ValidatedBase(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("Identity:Email:BaseUrl must be an absolute address without credentials.");
        var local = uri.Host is "localhost" or "127.0.0.1";
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local))
            throw new InvalidOperationException("Identity:Email:BaseUrl must use https (http is allowed for localhost only).");
        return uri;
    }

    public Task SendPasswordResetEmailAsync(string email, string resetToken, CancellationToken ct = default)
        => SendAsync(email, "Reset your password",
            $"Use this link to set a new password: {Link(PasswordResetPath, "token", resetToken)}\n\nIf you did not ask for this, ignore this message.", ct);

    public Task SendEmailConfirmationEmailAsync(string email, string confirmationToken, CancellationToken ct = default)
        => SendAsync(email, "Confirm your email address",
            $"Confirm this address: {Link(EmailConfirmationPath, "token", confirmationToken)}", ct);

    public Task SendEmailChangeEmailAsync(string newEmail, string changeToken, CancellationToken ct = default)
        => SendAsync(newEmail, "Confirm your new email address",
            $"Confirm this new address for your account: {Link(EmailChangePath, "NewEmail", newEmail, "Token", changeToken)}", ct);

    public Task SendWorkEmailVerificationAsync(string workEmail, string verificationToken, CancellationToken ct = default)
        => SendAsync(workEmail, "Confirm your work email address",
            $"Confirm that this is your work email address. It becomes your work email only after you confirm it: {Link(WorkEmailPath, "token", verificationToken)}\n\n" +
            "The link works once and expires after 30 minutes. If you did not expect this, ignore this message.", ct);

    private string Link(string path, params string[] nameValues)
    {
        var builder = new System.Text.StringBuilder(ValidatedBase(options.Value.BaseUrl).ToString().TrimEnd('/')).Append(path).Append('?');
        for (var i = 0; i < nameValues.Length; i += 2)
        {
            if (i > 0)
                builder.Append('&');
            builder.Append(nameValues[i]).Append('=').Append(Uri.EscapeDataString(nameValues[i + 1]));
        }
        return builder.ToString();
    }

    private async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("Identity:Email:FromAddress is required to send identity mail.");
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName ?? settings.FromAddress),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };
        message.To.Add(new MailAddress(to));
        await transport.SendAsync(message, ct);
    }
}
