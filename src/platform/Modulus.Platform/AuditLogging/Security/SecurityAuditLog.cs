namespace Modulus.AuditLogging.Security;

using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;

/// <summary>
/// The <see cref="ISecurityAuditLog"/> of <c>AddModulusSecurityAudit</c>: <see cref="Record"/> queues the event
/// (stamping the time and the ambient correlation id) and <see cref="SecurityAuditWriter"/> appends the queue to
/// the <see cref="ISecurityAuditStore"/> in order. One writer per process keeps appends from racing; a durable
/// store's unique (chain, sequence) key covers several replicas.
/// </summary>
/// <remarks>
/// The queue is in memory: without <see cref="SecurityAuditOptions.SpoolFile"/>, events recorded in the instant before a
/// crash can be lost. A full queue (<see cref="SecurityAuditOptions.QueueCapacity"/>) drops the new event and logs an
/// error, rather than blocking the request that produced it; with a spool file the event stays journaled and is appended
/// at the next start.
/// </remarks>
public sealed class SecurityAuditLog : ISecurityAuditLog, IDisposable
{
    private readonly Channel<QueuedSecurityAuditEvent> _queue;
    private readonly TimeProvider _clock;
    private readonly ICorrelationContext? _correlation;
    private readonly ILogger<SecurityAuditLog> _logger;
    private readonly SecurityAuditSpool? _spool;

    public SecurityAuditLog(
        IOptions<SecurityAuditOptions> options, TimeProvider clock, ILogger<SecurityAuditLog> logger,
        ICorrelationContext? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _spool = options.Value.SpoolFile is { Length: > 0 } path ? new SecurityAuditSpool(path) : null;
        var pending = _spool?.Pending ?? [];
        _queue = Channel.CreateBounded<QueuedSecurityAuditEvent>(
            new BoundedChannelOptions(Math.Max(1, options.Value.QueueCapacity) + pending.Count)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });
        _clock = clock;
        _logger = logger;
        _correlation = correlation;

        foreach (var (id, auditEvent) in pending)
            _queue.Writer.TryWrite(new QueuedSecurityAuditEvent(id, auditEvent));
        if (pending.Count > 0)
            _logger.LogWarning("Security audit: {Count} event(s) journaled by an earlier run are appended again", pending.Count);
    }

    internal ChannelReader<QueuedSecurityAuditEvent> Reader => _queue.Reader;

    /// <inheritdoc />
    public void Record(SecurityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var stamped = auditEvent with
        {
            OccurredAt = auditEvent.OccurredAt ?? _clock.GetUtcNow(),
            CorrelationId = auditEvent.CorrelationId ?? (_correlation is { IsSet: true } ? _correlation.CorrelationId : null),
        };
        var id = global::Modulus.GuidV7.Create();
        var spooled = _spool?.TryAppend(id, stamped) ?? false;
        if (_spool is not null && !spooled)
            _logger.LogError("Security audit spool could not be written; {Category}/{Action} is queued in memory only", stamped.Category, stamped.Action);

        if (!_queue.Writer.TryWrite(new QueuedSecurityAuditEvent(id, stamped)))
        {
            _logger.LogError(
                spooled
                    ? "Security audit queue is full; {Category}/{Action} for tenant {TenantId} stays in the spool for the next start"
                    : "Security audit queue is full; dropped {Category}/{Action} for tenant {TenantId}",
                stamped.Category, stamped.Action, stamped.TenantId);
        }
    }

    /// <summary>Stops accepting events (the writer then drains what is queued).</summary>
    internal void Complete() => _queue.Writer.TryComplete();

    /// <summary>The store holds the event: drop it from the spool.</summary>
    internal void Stored(Guid id) => _spool?.Acknowledge(id);

    /// <summary>Whether a lost event is still journaled for the next start.</summary>
    internal bool Spooled => _spool is not null;

    /// <inheritdoc />
    public void Dispose() => _spool?.Dispose();
}

/// <summary>A queued event and its spool id.</summary>
internal readonly record struct QueuedSecurityAuditEvent(Guid Id, SecurityAuditEvent Event);

/// <summary>Appends queued security audit events to the store, retrying a failed append.</summary>
public sealed class SecurityAuditWriter(
    SecurityAuditLog log, ISecurityAuditStore store, IOptions<SecurityAuditOptions> options, ILogger<SecurityAuditWriter> logger)
    : BackgroundService
{
    private static readonly TimeSpan s_maxDelay = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await log.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (log.Reader.TryRead(out var queued))
                    await AppendAsync(queued, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Drain what was recorded before shutdown, within the host's shutdown timeout.
        log.Complete();
        while (!cancellationToken.IsCancellationRequested && log.Reader.TryRead(out var queued))
            await AppendAsync(queued, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendAsync(QueuedSecurityAuditEvent queued, CancellationToken ct)
    {
        var auditEvent = queued.Event;
        var attempts = Math.Max(1, options.Value.MaxAppendAttempts);
        var delay = TimeSpan.FromMilliseconds(200);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await store.AppendAsync(auditEvent, ct).ConfigureAwait(false);
                log.Stored(queued.Id);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < attempts)
            {
                logger.LogWarning(ex, "Security audit append failed (attempt {Attempt}); retrying", attempt);
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, s_maxDelay.Ticks));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogCritical(
                    ex,
                    log.Spooled
                        ? "Security audit event {Category}/{Action} for tenant {TenantId} failed {Attempts} times; it stays in the spool for the next start"
                        : "Security audit event {Category}/{Action} for tenant {TenantId} was lost after {Attempts} attempts",
                    auditEvent.Category, auditEvent.Action, auditEvent.TenantId, attempt);
                return;
            }
        }
    }
}

/// <summary>Settings of the security audit (<c>Security:Audit</c>).</summary>
public sealed class SecurityAuditOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Security:Audit";

    /// <summary>Events held in memory before the writer stores them (default 10 000).</summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>Attempts per event before it is reported lost (default 5).</summary>
    public int MaxAppendAttempts { get; set; } = 5;

    /// <summary>How often the chain heads are anchored; zero or less disables anchoring (default 1 hour).</summary>
    public TimeSpan AnchorInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// A file the heads are appended to as JSON lines (<see cref="FileAuditAnchorSink"/>). Keep it somewhere the
    /// application's database role cannot reach, or add your own <see cref="IAuditAnchorSink"/>.
    /// </summary>
    public string? AnchorFile { get; set; }

    /// <summary>
    /// A local file every event is journaled to before it is queued (and acknowledged once stored), so events recorded
    /// just before a crash, or refused by the store, are appended at the next start (at least once: an event stored just
    /// before its acknowledgement was written appears twice). Null (default) keeps the queue in memory only. One file per
    /// process; replicas must not share it.
    /// </summary>
    public string? SpoolFile { get; set; }
}
