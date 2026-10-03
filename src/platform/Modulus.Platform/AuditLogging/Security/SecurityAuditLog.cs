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
/// The queue is in memory: events recorded in the instant before a crash can be lost. A full queue
/// (<see cref="SecurityAuditOptions.QueueCapacity"/>) drops the new event and logs an error, rather than
/// blocking the request that produced it.
/// </remarks>
public sealed class SecurityAuditLog : ISecurityAuditLog
{
    private readonly Channel<SecurityAuditEvent> _queue;
    private readonly TimeProvider _clock;
    private readonly ICorrelationContext? _correlation;
    private readonly ILogger<SecurityAuditLog> _logger;

    public SecurityAuditLog(
        IOptions<SecurityAuditOptions> options, TimeProvider clock, ILogger<SecurityAuditLog> logger,
        ICorrelationContext? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _queue = Channel.CreateBounded<SecurityAuditEvent>(new BoundedChannelOptions(Math.Max(1, options.Value.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
        _clock = clock;
        _logger = logger;
        _correlation = correlation;
    }

    internal ChannelReader<SecurityAuditEvent> Reader => _queue.Reader;

    /// <inheritdoc />
    public void Record(SecurityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var stamped = auditEvent with
        {
            OccurredAt = auditEvent.OccurredAt ?? _clock.GetUtcNow(),
            CorrelationId = auditEvent.CorrelationId ?? (_correlation is { IsSet: true } ? _correlation.CorrelationId : null),
        };
        if (!_queue.Writer.TryWrite(stamped))
            _logger.LogError(
                "Security audit queue is full; dropped {Category}/{Action} for tenant {TenantId}",
                stamped.Category, stamped.Action, stamped.TenantId);
    }

    /// <summary>Stops accepting events (the writer then drains what is queued).</summary>
    internal void Complete() => _queue.Writer.TryComplete();
}

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
                while (log.Reader.TryRead(out var auditEvent))
                    await AppendAsync(auditEvent, stoppingToken).ConfigureAwait(false);
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
        while (!cancellationToken.IsCancellationRequested && log.Reader.TryRead(out var auditEvent))
            await AppendAsync(auditEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendAsync(SecurityAuditEvent auditEvent, CancellationToken ct)
    {
        var attempts = Math.Max(1, options.Value.MaxAppendAttempts);
        var delay = TimeSpan.FromMilliseconds(200);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await store.AppendAsync(auditEvent, ct).ConfigureAwait(false);
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
                    ex, "Security audit event {Category}/{Action} for tenant {TenantId} was lost after {Attempts} attempts",
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
}
