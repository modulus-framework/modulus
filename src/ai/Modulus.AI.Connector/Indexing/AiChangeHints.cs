namespace Modulus.AI.Connector.Indexing;

using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Data;

/// <summary>
/// Signs change hints the Standard Webhooks way (<c>webhook-id</c>, <c>webhook-timestamp</c>,
/// <c>webhook-signature: v1,&lt;base64 HMAC-SHA256 of "{id}.{timestamp}.{body}"&gt;</c>), which the platform verifies
/// within a 5-minute window and de-duplicates by id (SEC-16).
/// </summary>
internal static class AiChangeHintSigner
{
    private const string Prefix = "whsec_";

    /// <summary>Reads a <c>whsec_</c> + base64 (or plain base64) secret of at least 16 bytes.</summary>
    public static bool TryReadSecret(string? secret, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(secret))
            return false;
        var encoded = secret.StartsWith(Prefix, StringComparison.Ordinal) ? secret[Prefix.Length..] : secret;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        return key.Length >= 16;
    }

    /// <summary>The <c>webhook-signature</c> value of <paramref name="body"/>.</summary>
    public static string Sign(byte[] key, string id, long timestamp, string body)
    {
        var payload = Encoding.UTF8.GetBytes($"{id}.{timestamp.ToString(CultureInfo.InvariantCulture)}.{body}");
        return "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, payload));
    }
}

/// <summary>
/// Tells the platform when an app instance has new changes (<see cref="AiChangeHintOptions"/>): every
/// <see cref="AiChangeHintOptions.Interval"/> it reads the journal head of each instance's company and, when it moved,
/// posts a signed <see cref="ChangeHint"/> (ids only, never data, AD-27). A hint the platform did not accept is sent again
/// on the next check until it is (at least once while the process lives; a hint lost to a restart only delays indexing until
/// the platform's own schedule calls <c>/changes</c>).
/// </summary>
internal sealed class AiChangeHintService(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IOptions<ModulusAiConnectorOptions> options,
    TimeProvider time,
    ILogger<AiChangeHintService> logger) : BackgroundService
{
    /// <summary>The named HTTP client hints are sent with.</summary>
    public const string HttpClientName = "Modulus.AI.Connector.ChangeHints";

    private readonly Dictionary<string, string> _heads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unsent = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.Indexing.ChangeHints.Enabled
            || !AiChangeHintSigner.TryReadSecret(settings.Platform.WebhookSecret, out var key))
            return;

        using var timer = new PeriodicTimer(settings.Indexing.ChangeHints.Interval, time);
        do
        {
            try
            {
                await CheckAsync(settings, key, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "AI connector: checking for changes to hint failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Reads every instance's journal head and hints the ones that moved since the last check.</summary>
    internal async Task CheckAsync(ModulusAiConnectorOptions settings, byte[] key, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IAiChangeFeed>() is not { } feed)
            return;

        foreach (var instance in settings.Instances)
        {
            var head = await feed.GetHeadAsync(instance.TenantId ?? Guid.Empty, ct);
            var moved = _heads.TryGetValue(instance.AppInstanceId, out var previous) && previous != head;
            _heads[instance.AppInstanceId] = head;
            if (moved || _unsent.Contains(instance.AppInstanceId))
            {
                if (await SendAsync(settings, key, instance.AppInstanceId, ct))
                    _unsent.Remove(instance.AppInstanceId);
                else
                    _unsent.Add(instance.AppInstanceId);
            }
        }
    }

    private async Task<bool> SendAsync(ModulusAiConnectorOptions settings, byte[] key, string appInstanceId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var id = "hint_" + Guid.CreateVersion7(now).ToString("N");
        var body = JsonSerializer.Serialize(new ChangeHint(appInstanceId, id, now), ConnectorJson.Options);
        var timestamp = now.ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Platform.BaseUrl!.TrimEnd('/') + settings.Indexing.ChangeHints.Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("webhook-id", id);
        request.Headers.Add("webhook-timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("webhook-signature", AiChangeHintSigner.Sign(key, id, timestamp, body));
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", settings.Platform.ApiKey);

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return true;
            logger.LogWarning("AI connector: the platform answered {Status} to a change hint for {AppInstance}; it is sent again.",
                (int)response.StatusCode, appInstanceId);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "AI connector: a change hint for {AppInstance} could not be sent; it is sent again.", appInstanceId);
        }

        return false;
    }
}
