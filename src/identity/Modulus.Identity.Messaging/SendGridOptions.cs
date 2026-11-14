namespace Modulus.Identity.Messaging;

/// <summary>SendGrid settings (<c>Identity:Email:SendGrid</c>). The API key is a secret: user secrets or the environment only.</summary>
public sealed class SendGridOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Identity:Email:SendGrid";

    /// <summary>The SendGrid API key (starts with <c>SG.</c>). Required; mail is refused without it.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The API base address. Default <c>https://api.sendgrid.com/</c>; the EU region uses <c>https://api.eu.sendgrid.com/</c>.</summary>
    public string BaseUrl { get; set; } = "https://api.sendgrid.com/";
}
