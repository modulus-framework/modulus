namespace Modulus.AI.Connector.Revocation;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Execution;
using Modulus.Core.Abstractions;

/// <summary>
/// Revocation signals waiting for the platform's acknowledgement, one per revocation key: a newer signal for a key
/// replaces the older one (the platform drops every cached scope of the key either way).
/// </summary>
/// <remarks>
/// In memory unless <see cref="AiPlatformOptions.RevocationSpoolFile"/> is set: then every change to the pending set is
/// written to that file (replaced atomically) and read back at start, so signals pending when the process stops are sent
/// after the restart. Without it they are lost; the platform also expires every cached scope after at most five minutes
/// (FR-17), which bounds the effect.
/// </remarks>
internal sealed class AiRevocationQueue
{
    private readonly string? _spoolFile;
    private readonly object _spoolGate = new();

    public AiRevocationQueue(IOptions<ModulusAiConnectorOptions>? options = null)
    {
        _spoolFile = options?.Value.Platform.RevocationSpoolFile;
        if (string.IsNullOrWhiteSpace(_spoolFile) || !File.Exists(_spoolFile))
            return;

        try
        {
            var saved = System.Text.Json.JsonSerializer.Deserialize<RevocationSignal[]>(File.ReadAllText(_spoolFile), ConnectorJson.Options);
            foreach (var signal in saved ?? [])
                _pending[signal.RevocationKey] = signal;
            if (!_pending.IsEmpty)
                _wake.Writer.TryWrite(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // An unreadable spool is treated as empty; the platform's scope expiry covers what it held.
        }
    }

    private readonly ConcurrentDictionary<string, RevocationSignal> _pending = new(StringComparer.Ordinal);
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>The signals not yet acknowledged.</summary>
    public int Count => _pending.Count;

    public void Enqueue(RevocationSignal signal)
    {
        _pending[signal.RevocationKey] = signal;
        Persist();
        _wake.Writer.TryWrite(true);
    }

    /// <summary>The oldest pending signal.</summary>
    public bool TryPeek(out RevocationSignal signal)
    {
        signal = _pending.Values.OrderBy(s => s.OccurredAt).FirstOrDefault()!;
        return signal is not null;
    }

    /// <summary>Removes <paramref name="signal"/> unless a newer one replaced it meanwhile.</summary>
    public void Acknowledge(RevocationSignal signal)
    {
        if (_pending.TryRemove(new KeyValuePair<string, RevocationSignal>(signal.RevocationKey, signal)))
            Persist();
    }

    private void Persist()
    {
        if (string.IsNullOrWhiteSpace(_spoolFile))
            return;

        lock (_spoolGate)
        {
            try
            {
                if (_pending.IsEmpty)
                {
                    File.Delete(_spoolFile);
                    return;
                }

                var temp = _spoolFile + ".tmp";
                File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(_pending.Values.ToArray(), ConnectorJson.Options));
                File.Move(temp, _spoolFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The journal is best effort: the in-memory queue still delivers while the process lives.
            }
        }
    }

    public ValueTask<bool> WaitAsync(CancellationToken ct) => _wake.Reader.ReadAsync(ct);
}

/// <summary>
/// Sends each pending signal to the platform's <c>POST /revocations/scope</c> with the connector's API key, and
/// retries it with the same payload and a growing back-off (capped by
/// <see cref="AiPlatformOptions.RevocationMaxBackoff"/>) until the platform answers 2xx (AD-12): the call is required,
/// not best-effort.
/// </summary>
internal sealed class AiRevocationDispatcher(
    AiRevocationQueue queue,
    IHttpClientFactory httpClients,
    IOptions<ModulusAiConnectorOptions> options,
    TimeProvider clock,
    ILogger<AiRevocationDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "Modulus.AI.Connector.Revocation";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!queue.TryPeek(out var signal))
                {
                    await queue.WaitAsync(stoppingToken);
                    continue;
                }

                await DeliverAsync(signal, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task DeliverAsync(RevocationSignal signal, CancellationToken ct)
    {
        var platform = options.Value.Platform;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(platform.BaseUrl!.TrimEnd('/') + "/"), "revocations/scope"))
                {
                    Content = JsonContent.Create(signal, options: ConnectorJson.Options),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", platform.ApiKey);

                using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    queue.Acknowledge(signal);
                    return;
                }

                logger.LogWarning("The AI platform answered {Status} to revocation signal {Key}; retrying.",
                    (int)response.StatusCode, signal.RevocationKey);
            }
            catch (Exception ex) when (ex is HttpRequestException or UriFormatException
                || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Sending revocation signal {Key} to the AI platform failed; retrying.", signal.RevocationKey);
            }

            var backoff = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attempt, 16)));
            var delay = backoff < platform.RevocationMaxBackoff ? backoff : platform.RevocationMaxBackoff;
            await Task.Delay(delay * (0.8 + (Random.Shared.NextDouble() * 0.4)), clock, ct);
        }
    }
}

/// <summary>
/// Turns every access change into a revocation signal for the app instances of the affected company: all instances
/// when the change applies to the host or to every company (no company in the change or in scope).
/// </summary>
internal sealed class AiRevocationObserver(
    AiRevocationQueue queue,
    IOptions<ModulusAiConnectorOptions> options,
    ICurrentTenant? currentTenant = null) : IAccessChangeObserver
{
    public ValueTask OnAccessChangedAsync(AccessChange change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var tenantId = change.TenantId ?? currentTenant?.TenantId;
        foreach (var instance in options.Value.Instances)
        {
            if (tenantId is null || instance.TenantId is null || instance.TenantId == tenantId)
            {
                queue.Enqueue(new RevocationSignal(
                    RevocationKeys.For(instance.AppInstanceId), instance.AppInstanceId, change.Reason, change.OccurredAt));
            }
        }

        return ValueTask.CompletedTask;
    }
}
