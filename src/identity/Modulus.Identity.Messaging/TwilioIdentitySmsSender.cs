namespace Modulus.Identity.Messaging;

using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

/// <summary>
/// Sends SMS through Twilio's REST API (<c>Messages.json</c>, HTTP basic authentication with the account SID and auth token).
/// The phone number and the message are not part of any error text.
/// </summary>
public sealed partial class TwilioIdentitySmsSender(HttpClient http, IOptions<TwilioOptions> options) : IIdentitySmsSender
{
    public async Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(phoneNumber);
        ArgumentException.ThrowIfNullOrEmpty(message);
        if (!E164().IsMatch(phoneNumber))
            throw new ArgumentException("The phone number must be in E.164 form, for example +15555550100.", nameof(phoneNumber));

        var settings = options.Value;
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", phoneNumber),
            new("Body", message),
        };
        if (!string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            form.Add(new("MessagingServiceSid", settings.MessagingServiceSid));
        else
            form.Add(new("From", settings.FromNumber!));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"2010-04-01/Accounts/{Uri.EscapeDataString(settings.AccountSid!)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twilio refused the message with status {(int)response.StatusCode}.");
    }

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164();
}
