namespace Modulus.Analytics;

/// <summary>Chains <see cref="IAnalyticsRecordUpcaster"/> steps to bring a record to a target dataset version.</summary>
public static class AnalyticsUpcasting
{
    /// <summary>
    /// Returns <paramref name="record"/> at <paramref name="targetVersion"/>. A record already at that version is returned as is.
    /// </summary>
    /// <param name="upcasters">The available steps; steps for other datasets are ignored.</param>
    /// <param name="record">The record to upgrade.</param>
    /// <param name="targetVersion">The version to reach.</param>
    /// <exception cref="InvalidOperationException">The record is newer than the target, or a step in between is missing.</exception>
    public static AnalyticsRecord UpcastTo(IEnumerable<IAnalyticsRecordUpcaster> upcasters, AnalyticsRecord record, int targetVersion)
    {
        ArgumentNullException.ThrowIfNull(upcasters);
        ArgumentNullException.ThrowIfNull(record);

        if (record.SchemaVersion > targetVersion)
        {
            throw new InvalidOperationException(
                $"Record for '{record.Dataset}' is at version {record.SchemaVersion}, newer than the registered version {targetVersion}.");
        }

        var steps = upcasters.Where(u => u.Dataset == record.Dataset).ToDictionary(u => u.FromVersion);
        var current = record;
        while (current.SchemaVersion < targetVersion)
        {
            if (!steps.TryGetValue(current.SchemaVersion, out var step))
            {
                throw new InvalidOperationException(
                    $"No upcaster for '{record.Dataset}' from version {current.SchemaVersion} to {current.SchemaVersion + 1}.");
            }

            current = current with { Values = step.Upcast(current.Values), SchemaVersion = current.SchemaVersion + 1 };
        }

        return current;
    }
}
