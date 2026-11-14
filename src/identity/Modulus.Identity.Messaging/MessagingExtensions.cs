namespace Modulus.Identity.Messaging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Email;

/// <summary>Registration of the mail and SMS providers. Each option set is validated when it is first used, so a missing key fails at startup.</summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Sends identity mail through SendGrid. Links come from <c>Identity:Email:BaseUrl</c>, the sender from
    /// <c>Identity:Email:FromAddress</c>, the key from <c>Identity:Email:SendGrid:ApiKey</c>. Call it <i>before</i>
    /// <c>AddModulusIdentity</c>: this registration takes the sender slot, and the identity defaults only fill empty slots.
    /// </summary>
    public static IServiceCollection AddModulusSendGridEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdentityEmailOptions>(configuration.GetSection(IdentityEmailOptions.SectionName));
        services.AddOptions<SendGridOptions>()
            .Bind(configuration.GetSection(SendGridOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Identity:Email:SendGrid:ApiKey is required.")
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "Identity:Email:SendGrid:BaseUrl must be an https address.")
            .ValidateOnStart();

        services.AddHttpClient<IIdentityEmailTransport, SendGridIdentityEmailTransport>((sp, client) =>
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<SendGridOptions>>().Value.BaseUrl));
        services.TryAddScoped<IIdentityEmailSender>(sp => ActivatorUtilities.CreateInstance<SmtpIdentityEmailSender>(sp));
        return services;
    }

    /// <summary>
    /// Sends SMS through Twilio: <c>Identity:Sms:Twilio</c> with the account SID, auth token, and either a sender number or a
    /// Messaging Service SID.
    /// </summary>
    public static IServiceCollection AddModulusTwilioSms(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioOptions>()
            .Bind(configuration.GetSection(TwilioOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.AccountSid), "Identity:Sms:Twilio:AccountSid is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthToken), "Identity:Sms:Twilio:AuthToken is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.FromNumber) || !string.IsNullOrWhiteSpace(o.MessagingServiceSid),
                "Set Identity:Sms:Twilio:FromNumber or Identity:Sms:Twilio:MessagingServiceSid.")
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "Identity:Sms:Twilio:BaseUrl must be an https address.")
            .ValidateOnStart();

        services.AddHttpClient<IIdentitySmsSender, TwilioIdentitySmsSender>((sp, client) =>
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<TwilioOptions>>().Value.BaseUrl));
        return services;
    }
}
