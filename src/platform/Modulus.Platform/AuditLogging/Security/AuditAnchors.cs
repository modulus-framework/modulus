namespace Modulus.AuditLogging.Security;

using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Somewhere outside the audit database that keeps the chain heads (a file on another volume, object storage with
/// a retention lock, a ticket, a transparency log). A chain recomputed after an edit no longer matches its anchor.
/// </summary>
public interface IAuditAnchorSink
{
    /// <summary>Keeps the heads anchored at <paramref name="anchoredAt"/>.</summary>
    Task WriteAsync(IReadOnlyList<SecurityAuditHead> heads, DateTimeOffset anchoredAt, CancellationToken ct = default);
}

/// <summary>Appends each anchor to a file as one JSON line (<c>{"at":…,"heads":[{"chain","sequence","hash"}]}</c>).</summary>
public sealed class FileAuditAnchorSink(string path) : IAuditAnchorSink
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The file the anchors are appended to.</summary>
    public string Path { get; } = path;

    /// <inheritdoc />
    public async Task WriteAsync(IReadOnlyList<SecurityAuditHead> heads, DateTimeOffset anchoredAt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(heads);
        var line = JsonSerializer.Serialize(new
        {
            at = anchoredAt.UtcDateTime,
            heads = heads.Select(h => new { chain = h.ChainId, sequence = h.Sequence, hash = h.Hash }),
        });

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) is { } directory)
                Directory.CreateDirectory(directory);
            await File.AppendAllTextAsync(Path, line + "\n", ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Writes the chain heads to every <see cref="IAuditAnchorSink"/> each <see cref="SecurityAuditOptions.AnchorInterval"/>,
/// when they changed since the last anchor.
/// </summary>
public sealed class AuditAnchorService(
    ISecurityAuditStore store,
    IEnumerable<IAuditAnchorSink> sinks,
    IOptions<SecurityAuditOptions> options,
    TimeProvider clock,
    ILogger<AuditAnchorService> logger) : BackgroundService
{
    private readonly IAuditAnchorSink[] _sinks = sinks.ToArray();
    private IReadOnlyList<SecurityAuditHead> _last = [];

    /// <summary>Anchors the current heads now if they changed. Returns whether anything was written.</summary>
    public async Task<bool> AnchorAsync(CancellationToken ct = default)
    {
        var heads = (await store.GetHeadsAsync(ct).ConfigureAwait(false)).OrderBy(h => h.ChainId).ToList();
        if (heads.Count == 0 || heads.SequenceEqual(_last))
            return false;

        var at = clock.GetUtcNow();
        foreach (var sink in _sinks)
            await sink.WriteAsync(heads, at, ct).ConfigureAwait(false);
        _last = heads;
        return true;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.AnchorInterval;
        if (interval <= TimeSpan.Zero || _sinks.Length == 0)
            return;

        using var timer = new PeriodicTimer(interval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await AnchorAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Anchoring the security audit chain heads failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
