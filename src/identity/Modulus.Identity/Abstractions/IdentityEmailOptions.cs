namespace Modulus.Identity.Abstractions;

/// <summary>
/// Where identity mail comes from and where its links point (<c>Identity:Email</c>). Set <see cref="BaseUrl"/> to the public address of the
/// web application; the links are built from it, so a link in a mail always leads to the page that handles it. <see cref="SmtpHost"/> turns
/// the SMTP sender on; without it, mail is not sent (the default no-op sender). Put <see cref="SmtpPassword"/> in user secrets or the
/// environment, never in a committed file.
/// </summary>
public sealed class IdentityEmailOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Identity:Email";

    /// <summary>The public base address of the web application, for example <c>https://shop.example.com</c>. Required with <see cref="SmtpHost"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The sender address shown on the mail.</summary>
    public string? FromAddress { get; set; }

    /// <summary>The sender name shown on the mail.</summary>
    public string? FromName { get; set; }

    /// <summary>The SMTP server. Null turns SMTP sending off.</summary>
    public string? SmtpHost { get; set; }

    /// <summary>The SMTP port. Default 587 (STARTTLS).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Whether the connection uses TLS. Default on; it may be off only for a local test server.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>The SMTP user name, when the server needs one.</summary>
    public string? SmtpUserName { get; set; }

    /// <summary>The SMTP password. A secret: user secrets or the environment only.</summary>
    public string? SmtpPassword { get; set; }
}
