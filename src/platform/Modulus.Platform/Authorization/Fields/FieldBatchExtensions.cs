namespace Modulus.Authorization.Fields;

/// <summary>
/// Field checks for the paths that move many records at once: an export returns rows, and a bulk import writes them. Both
/// decide the fields once for the whole batch, so a batch never passes a row that a single write would have refused.
/// </summary>
public static class FieldBatchExtensions
{
    /// <summary>
    /// Redacts each row of an export for the current caller and checks it (<see cref="FieldOutputGuard.EnsureMasked{T}"/>), so the
    /// export carries no field the caller may not read. Rows are projections: redaction resets their masked fields in place.
    /// </summary>
    /// <param name="authorizer">The current caller's field authorizer.</param>
    /// <param name="rows">The records to export.</param>
    public static List<T> RedactForExport<T>(this IFieldAuthorizer authorizer, IEnumerable<T> rows)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(rows);

        var exported = new List<T>();
        foreach (var row in rows)
        {
            var masked = authorizer.Redact(row);
            FieldOutputGuard.EnsureMasked(authorizer, masked);
            exported.Add(masked);
        }

        return exported;
    }

    /// <summary>
    /// All or nothing for a bulk write: the fields of every row are checked together before any row is written. One row that
    /// sets a field the caller may not write refuses the whole batch, with the same <c>FIELD_PROTECTED</c> denial as a single write.
    /// </summary>
    /// <param name="authorizer">The current caller's field authorizer.</param>
    /// <param name="type">The record type being written.</param>
    /// <param name="rows">For each row, the fields it sets.</param>
    /// <param name="ct">Cancels the check.</param>
    public static Task EnsureBatchWriteAllowedAsync(
        this IFieldAuthorizer authorizer, Type type, IEnumerable<IEnumerable<string>> rows, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(rows);

        var fields = rows.SelectMany(r => r).Distinct(StringComparer.Ordinal).ToList();
        return fields.Count == 0
            ? Task.CompletedTask
            : authorizer.EnsureWriteAllowedAsync(type, fields, ct);
    }
}
