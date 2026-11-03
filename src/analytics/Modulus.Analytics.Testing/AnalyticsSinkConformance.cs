namespace Modulus.Analytics.Testing;

/// <summary>One check in a conformance run.</summary>
/// <param name="Name">What was checked.</param>
/// <param name="Passed">Whether it held.</param>
/// <param name="Detail">For a failure, what was seen.</param>
public sealed record AnalyticsConformanceCheck(string Name, bool Passed, string? Detail = null);

/// <summary>The result of <see cref="AnalyticsSinkConformance.RunAsync"/>.</summary>
/// <param name="Checks">Every check, in order.</param>
public sealed record AnalyticsConformanceReport(IReadOnlyList<AnalyticsConformanceCheck> Checks)
{
    /// <summary>Whether every check passed.</summary>
    public bool Passed => Checks.All(c => c.Passed);

    /// <summary>The failed checks.</summary>
    public IEnumerable<AnalyticsConformanceCheck> Failures => Checks.Where(c => !c.Passed);

    /// <summary>A readable summary, one line per failure, for an assertion message.</summary>
    public override string ToString() => Passed
        ? $"{Checks.Count} checks passed."
        : string.Join(Environment.NewLine, Failures.Select(f => $"FAILED {f.Name}: {f.Detail}"));
}

/// <summary>
/// Checks that an <see cref="IAnalyticsSink"/> honours the contract the delivery pipeline relies on. Run it against every provider
/// with a real backing store (a container in an integration test), and against the in-memory sink as the reference. The suite is
/// test-framework neutral: assert on <see cref="AnalyticsConformanceReport.Passed"/>.
/// </summary>
public static class AnalyticsSinkConformance
{
    /// <summary>Runs the suite.</summary>
    /// <param name="sink">The sink under test.</param>
    /// <param name="dataset">A dataset the sink can store, matching the records <paramref name="makeRecord"/> builds.</param>
    /// <param name="countRows">
    /// Reads how many rows the store holds for a tenant and dataset. The sink interface is write-only, so the provider's own test
    /// supplies the read, for example a <c>SELECT count()</c>.
    /// </param>
    /// <param name="makeRecord">Builds a valid record for the dataset from an event id and a text value for its payload. The suite sets the tenant itself.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    public static async Task<AnalyticsConformanceReport> RunAsync(
        IAnalyticsSink sink,
        AnalyticsDatasetDescriptor dataset,
        Func<Guid, string, Task<long>> countRows,
        Func<Guid, string, AnalyticsRecord> makeRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(countRows);
        ArgumentNullException.ThrowIfNull(makeRecord);

        var checks = new List<AnalyticsConformanceCheck>();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await Check(checks, "Has a non-empty name", () => Task.FromResult(string.IsNullOrWhiteSpace(sink.Name) ? "name is empty" : null));

        await Check(checks, "EnsureDataset can be called twice", async () =>
        {
            await sink.EnsureDatasetAsync(dataset, cancellationToken);
            await sink.EnsureDatasetAsync(dataset, cancellationToken);
            return null;
        });

        await Check(checks, "Writing a batch stores each record", async () =>
        {
            var batch = new[] { makeRecord(Guid.NewGuid(), "a"), makeRecord(Guid.NewGuid(), "b") };
            var tenantBatch = batch.Select(r => r with { TenantId = tenantA }).ToList();
            await sink.WriteAsync(dataset, tenantBatch, cancellationToken);
            var count = await countRows(tenantA, dataset.Name);
            return count == 2 ? null : $"expected 2 rows, found {count}";
        });

        await Check(checks, "Writing the same record again does not duplicate it", async () =>
        {
            var record = makeRecord(Guid.NewGuid(), "dup") with { TenantId = tenantB };
            await sink.WriteAsync(dataset, [record], cancellationToken);
            await sink.WriteAsync(dataset, [record], cancellationToken);
            var count = await countRows(tenantB, dataset.Name);
            return count == 1 ? null : $"expected 1 row after a repeated write, found {count}";
        });

        await Check(checks, "Tenants are kept apart", async () =>
        {
            var a = await countRows(tenantA, dataset.Name);
            var b = await countRows(tenantB, dataset.Name);
            return a == 2 && b == 1 ? null : $"tenant A has {a} rows (expected 2), tenant B has {b} (expected 1)";
        });

        await Check(checks, "An empty batch is accepted", async () =>
        {
            await sink.WriteAsync(dataset, [], cancellationToken);
            return null;
        });

        return new AnalyticsConformanceReport(checks);
    }

    private static async Task Check(List<AnalyticsConformanceCheck> checks, string name, Func<Task<string?>> run)
    {
        try
        {
            var problem = await run().ConfigureAwait(false);
            checks.Add(new AnalyticsConformanceCheck(name, problem is null, problem));
        }
        catch (Exception ex)
        {
            checks.Add(new AnalyticsConformanceCheck(name, false, $"{ex.GetType().Name}: {ex.Message}"));
        }
    }
}
