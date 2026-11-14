namespace Modulus.Identity.Messaging;

/// <summary>
/// Sends a text message, for example a one-time code. The framework does not start SMS flows by itself; the application calls this
/// when it needs to reach a phone. Implementations must not log the phone number or the message text.
/// </summary>
public interface IIdentitySmsSender
{
    /// <summary>Sends <paramref name="message"/> to <paramref name="phoneNumber"/>, which must be in E.164 form (<c>+15555550100</c>).</summary>
    Task SendAsync(string phoneNumber, string message, CancellationToken ct = default);
}
