namespace Modulus.Identity.Messaging;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Modulus.Identity.Email;

/// <summary>
/// Delivers identity mail through SendGrid's v3 mail API. It is a transport: <see cref="SmtpIdentityEmailSender"/> builds the
/// message (links, subjects, bodies) and this class only sends it, so both providers word and link mail the same way.
/// One personalization per recipient, so recipients never see each other. The body and the token are never logged, and a
/// refusal reports only the status code.
/// </summary>
public sealed class SendGridIdentityEmailTransport(HttpClient http, IOptions<SendGridOptions> options) : IIdentityEmailTransport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task SendAsync(MailMessage message, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Identity:Email:SendGrid:ApiKey is required to send mail through SendGrid.");
        var from = message.From ?? throw new InvalidOperationException("The identity mail has no sender address.");

        var payload = new SendGridMail(
            message.To.Select(to => new SendGridPersonalization([new SendGridAddress(to.Address, to.DisplayName)])).ToArray(),
            new SendGridAddress(from.Address, from.DisplayName),
            message.Subject,
            [new SendGridContent("text/plain", message.Body)]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send")
        {
            Content = JsonContent.Create(payload, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SendGrid refused the mail with status {(int)response.StatusCode}.");
    }

    private sealed record SendGridAddress(string Email, string? Name);

    private sealed record SendGridPersonalization(SendGridAddress[] To);

    private sealed record SendGridContent(string Type, string Value);

    private sealed record SendGridMail(SendGridPersonalization[] Personalizations, SendGridAddress From, string Subject, SendGridContent[] Content);
}
