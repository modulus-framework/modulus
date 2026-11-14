namespace Modulus.Identity.Messaging;

/// <summary>Twilio settings (<c>Identity:Sms:Twilio</c>). The auth token is a secret: user secrets or the environment only.</summary>
public sealed class TwilioOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Identity:Sms:Twilio";

    /// <summary>The account SID (starts with <c>AC</c>).</summary>
    public string? AccountSid { get; set; }

    /// <summary>The auth token. A secret.</summary>
    public string? AuthToken { get; set; }

    /// <summary>The sender number in E.164 form, for example <c>+15555550100</c>. Set this or <see cref="MessagingServiceSid"/>.</summary>
    public string? FromNumber { get; set; }

    /// <summary>A Twilio Messaging Service SID (starts with <c>MG</c>). When set, it is used instead of <see cref="FromNumber"/>.</summary>
    public string? MessagingServiceSid { get; set; }

    /// <summary>The API base address. Default <c>https://api.twilio.com/</c>.</summary>
    public string BaseUrl { get; set; } = "https://api.twilio.com/";
}
