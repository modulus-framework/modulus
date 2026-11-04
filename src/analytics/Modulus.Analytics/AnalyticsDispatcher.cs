namespace Modulus.Analytics;

using System.Collections.Concurrent;
using Modulus.Core.Abstractions.DataProtection;

/// <summary>
/// Takes captured analytics records to every registered sink: it refuses records it cannot place (unknown dataset, no tenant),
/// upgrades old versions to the current contract, masks according to the dataset's metadata, and writes one batch per dataset to each
/// sink. A failing sink does not stop the others; each result is reported so the caller can retry or dead-letter.
/// </summary>
/// <remarks>
/// This is the delivery core. It holds no queue: a durable queue, with retry and dead-lettering, calls it. Delivery is at least
/// once, so sinks must tolerate the same record twice.
/// </remarks>
public sealed class AnalyticsDispatcher
{
    private readonly IAnalyticsDatasetCatalog _catalog;
    private readonly IReadOnlyList<IAnalyticsSink> _sinks;
    private readonly IReadOnlyList<IAnalyticsRecordUpcaster> _upcasters;
    private readonly IPersonalDataProtector _protector;
    private readonly ConcurrentDictionary<(string Sink, string Dataset), bool> _ensured = new();
    private readonly AnalyticsCircuitBreaker _breaker;

    /// <summary>Creates the dispatcher.</summary>
    /// <param name="catalog">The registered datasets.</param>
    /// <param name="sinks">The destinations.</param>
    /// <param name="upcasters">Version steps for old records.</param>
    /// <param name="protector">Supplies the keyed hash for masked fields.</param>
    /// <param name="breaker">Pauses writes to a sink that keeps failing. Without one, each sink is always called.</param>
    public AnalyticsDispatcher(
        IAnalyticsDatasetCatalog catalog,
        IEnumerable<IAnalyticsSink> sinks,
        IEnumerable<IAnalyticsRecordUpcaster> upcasters,
        IPersonalDataProtector protector,
        AnalyticsCircuitBreaker? breaker = null)
    {
        _breaker = breaker ?? new AnalyticsCircuitBreaker(
            Microsoft.Extensions.Options.Options.Create(new AnalyticsCircuitBreakerOptions()), TimeProvider.System);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _sinks = [.. sinks ?? throw new ArgumentNullException(nameof(sinks))];
        _upcasters = [.. upcasters ?? throw new ArgumentNullException(nameof(upcasters))];
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <summary>Whether any sink is registered. With none, a dispatch would drop every record, so callers with a queue should wait instead.</summary>
    public bool HasSinks => _sinks.Count > 0;

    /// <summary>Validates, upgrades, masks and delivers <paramref name="records"/>.</summary>
    /// <param name="records">The records, possibly of several datasets and tenants.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    public async Task<AnalyticsDispatchResult> DispatchAsync(IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var rejected = new List<AnalyticsRejection>();
        var ready = new Dictionary<string, (AnalyticsDatasetDescriptor Dataset, List<AnalyticsRecord> Records)>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (record.TenantId == Guid.Empty)
            {
                rejected.Add(new(record, "Analytics records must belong to a tenant."));
                continue;
            }

            var latest = _catalog.Latest(record.Dataset);
            if (latest is null)
            {
                rejected.Add(new(record, $"Dataset '{record.Dataset}' is not registered."));
                continue;
            }

            AnalyticsRecord upgraded;
            try
            {
                upgraded = AnalyticsUpcasting.UpcastTo(_upcasters, record, latest.Version);
            }
            catch (InvalidOperationException ex)
            {
                rejected.Add(new(record, ex.Message));
                continue;
            }

            var masked = AnalyticsMasking.Apply(latest, upgraded, _protector);
            if (!ready.TryGetValue(latest.Key, out var group))
            {
                group = (latest, []);
                ready[latest.Key] = group;
            }

            group.Records.Add(masked);
        }

        var outcomes = new List<AnalyticsSinkOutcome>();
        foreach (var (key, (dataset, batch)) in ready)
        {
            foreach (var sink in _sinks)
            {
                outcomes.Add(await WriteAsync(sink, key, dataset, batch, cancellationToken).ConfigureAwait(false));
            }
        }

        return new AnalyticsDispatchResult(ready.Values.Sum(g => g.Records.Count), rejected, outcomes);
    }

    private async Task<AnalyticsSinkOutcome> WriteAsync(
        IAnalyticsSink sink, string key, AnalyticsDatasetDescriptor dataset, List<AnalyticsRecord> batch, CancellationToken cancellationToken)
    {
        if (!_breaker.TryEnter(sink.Name))
        {
            // The records stay queued: the caller treats a transient outcome as a retry.
            AnalyticsMetrics.Failed.Add(batch.Count, AnalyticsMetrics.Tags(sink.Name, dataset.Name));
            return new AnalyticsSinkOutcome(sink.Name, key, batch.Count, false, true,
                $"Circuit open for sink '{sink.Name}': writes are paused until it recovers.");
        }

        var answered = false;
        try
        {
            if (!_ensured.ContainsKey((sink.Name, key)))
            {
                await sink.EnsureDatasetAsync(dataset, cancellationToken).ConfigureAwait(false);
                _ensured[(sink.Name, key)] = true;
            }

            await sink.WriteAsync(dataset, batch, cancellationToken).ConfigureAwait(false);
            answered = true;
            _breaker.RecordSuccess(sink.Name);
            AnalyticsMetrics.Delivered.Add(batch.Count, AnalyticsMetrics.Tags(sink.Name, dataset.Name));
            return new AnalyticsSinkOutcome(sink.Name, key, batch.Count, true);
        }
        catch (AnalyticsSinkException ex)
        {
            answered = true;
            if (ex.IsTransient)
            {
                _breaker.RecordTransientFailure(sink.Name);
            }
            else
            {
                _breaker.RecordSuccess(sink.Name);
            }

            AnalyticsMetrics.Failed.Add(batch.Count, AnalyticsMetrics.Tags(sink.Name, dataset.Name));
            return new AnalyticsSinkOutcome(sink.Name, key, batch.Count, false, ex.IsTransient, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unexpected provider error is treated as transient: a sink that knows better throws AnalyticsSinkException.
            answered = true;
            _breaker.RecordTransientFailure(sink.Name);
            AnalyticsMetrics.Failed.Add(batch.Count, AnalyticsMetrics.Tags(sink.Name, dataset.Name));
            return new AnalyticsSinkOutcome(sink.Name, key, batch.Count, false, true, ex.Message);
        }
        finally
        {
            if (!answered)
            {
                _breaker.Release(sink.Name);
            }
        }
    }
}
