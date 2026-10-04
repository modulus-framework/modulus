namespace Modulus.AI.Connector.EntityFrameworkCore;

using System.Buffers.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.AI.Connector.Data;

/// <summary>
/// Reads the <c>ai_changes</c> journal of every module context. Each context numbers its own rows, so the cursor holds
/// one position per context (base64url JSON); contexts are merged oldest first without ever skipping a row of one.
/// </summary>
internal sealed class EfAiChangeFeed(IEnumerable<DbContext> contexts) : IAiChangeFeed
{
    public async Task<AiJournalPage> ReadAsync(
        Guid tenantId, string? cursor, int max, DateTimeOffset settledBefore, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        var positions = Decode(cursor);
        var settled = settledBefore.UtcDateTime;

        // Per context: the next rows in sequence order, cut at the first unsettled one (a lower sequence may still be
        // committing behind it, so nothing after it is safe to hand out yet).
        var queues = new List<(string Name, Queue<AiChangeRecord> Rows)>();
        var more = false;
        foreach (var context in Journaled())
        {
            var name = NameOf(context);
            var after = positions.GetValueOrDefault(name);
            var rows = await context.Set<AiChangeRecord>().AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Sequence > after)
                .OrderBy(c => c.Sequence)
                .Take(max + 1)
                .ToListAsync(ct);
            var ready = rows.TakeWhile(r => r.OccurredAt <= settled).ToList();
            more |= ready.Count > max;
            queues.Add((name, new Queue<AiChangeRecord>(ready)));
        }

        var changes = new List<AiJournalChange>(max);
        while (changes.Count < max)
        {
            var next = queues.Where(q => q.Rows.Count > 0).OrderBy(q => q.Rows.Peek().OccurredAt).FirstOrDefault();
            if (next.Rows is null)
                break;

            var row = next.Rows.Dequeue();
            positions[next.Name] = row.Sequence;
            changes.Add(new AiJournalChange(
                row.ResourceType, row.ResourceId, row.Kind, new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc))));
        }

        return new AiJournalPage(changes, Encode(positions), more || queues.Any(q => q.Rows.Count > 0));
    }

    public async Task<string> GetHeadAsync(Guid tenantId, CancellationToken ct = default)
    {
        var positions = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var context in Journaled())
        {
            var head = await context.Set<AiChangeRecord>().AsNoTracking()
                .Where(c => c.TenantId == tenantId)
                .MaxAsync(c => (long?)c.Sequence, ct);
            if (head is { } sequence)
                positions[NameOf(context)] = sequence;
        }

        return Encode(positions);
    }

    private IEnumerable<DbContext> Journaled()
        => contexts.Where(c => c.Model.FindEntityType(typeof(AiChangeRecord)) is not null).DistinctBy(c => c.GetType());

    private static string NameOf(DbContext context) => context.GetType().Name;

    // Never empty, even before the first change: the platform stores the cursor and sends it back as since=, and an
    // empty value would read as "no cursor".
    internal static string Encode(Dictionary<string, long> positions)
        => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(positions));

    internal static Dictionary<string, long> Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return new(StringComparer.Ordinal);
        try
        {
            var positions = JsonSerializer.Deserialize<Dictionary<string, long>>(Base64Url.DecodeFromChars(cursor))
                ?? throw new FormatException("Invalid cursor.");
            return new(positions, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new FormatException("Invalid cursor.", ex);
        }
    }
}
