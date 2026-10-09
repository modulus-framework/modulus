namespace Modulus.AuditLogging.Security;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Modulus.Core.Abstractions;

/// <summary>
/// The hashing rules of the security audit chain. Every store uses them, so an entry written by one store
/// verifies with another.
/// </summary>
/// <remarks>
/// The hash is SHA-256 over a canonical JSON object: properties in ordinal key order (details too), no
/// whitespace, timestamps as UTC <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>, the sequence as a number and the previous hash
/// as a string. Changing these rules invalidates every stored chain.
/// </remarks>
public static class SecurityAuditChain
{
    /// <summary>The <see cref="SecurityAuditRecord.PreviousHash"/> of a chain's first entry.</summary>
    public static readonly string GenesisHash = new('0', 64);

    /// <summary>The chain an event belongs to: its tenant, or <see cref="Guid.Empty"/> for the host.</summary>
    public static Guid ChainOf(Guid? tenantId) => tenantId ?? Guid.Empty;

    /// <summary>Truncates to whole milliseconds (every supported database keeps at least that).</summary>
    public static DateTimeOffset Normalize(DateTimeOffset value)
    {
        var utc = value.UtcTicks;
        return new DateTimeOffset(utc - (utc % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
    }

    /// <summary>Sequences and hashes <paramref name="auditEvent"/> after <paramref name="previous"/> (null = first entry).</summary>
    public static SecurityAuditRecord Link(SecurityAuditEvent auditEvent, SecurityAuditHead? previous, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var unhashed = new SecurityAuditRecord
        {
            ChainId = ChainOf(auditEvent.TenantId),
            Sequence = (previous?.Sequence ?? 0) + 1,
            OccurredAt = Normalize(auditEvent.OccurredAt ?? now),
            Category = auditEvent.Category,
            Action = auditEvent.Action,
            Outcome = auditEvent.Outcome,
            Actor = auditEvent.Actor,
            Target = auditEvent.Target,
            CorrelationId = auditEvent.CorrelationId,
            Details = auditEvent.Details,
            PreviousHash = previous?.Hash ?? GenesisHash,
            Hash = string.Empty,
        };
        return unhashed with { Hash = ComputeHash(unhashed) };
    }

    /// <summary>The hash <paramref name="record"/> should carry (its own <see cref="SecurityAuditRecord.Hash"/> is ignored).</summary>
    public static string ComputeHash(SecurityAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("action", record.Action);
            json.WriteString("actor", record.Actor);
            json.WriteString("category", record.Category);
            json.WriteString("chain", record.ChainId.ToString("D"));
            json.WriteString("correlationId", record.CorrelationId);
            json.WriteStartObject("details");
            foreach (var (key, value) in record.Details.OrderBy(d => d.Key, StringComparer.Ordinal))
                json.WriteString(key, value);
            json.WriteEndObject();
            json.WriteString(
                "occurredAt", Normalize(record.OccurredAt).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            json.WriteString("outcome", record.Outcome);
            json.WriteString("previousHash", record.PreviousHash);
            json.WriteNumber("sequence", record.Sequence);
            json.WriteString("target", record.Target);
            json.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    /// <summary>
    /// Checks a chain read in sequence order: contiguous sequences from 1, each hash recomputed, each entry linked to
    /// the previous one, and, when <paramref name="anchor"/> is given, the anchored entry still present and unchanged
    /// (a chain recomputed from scratch after an edit passes the link checks but not the anchor).
    /// </summary>
    public static SecurityAuditVerification Verify(IEnumerable<SecurityAuditRecord> records, SecurityAuditHead? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        SecurityAuditHead? head = null;
        long count = 0;
        var anchorSeen = false;
        foreach (var record in records)
        {
            count++;
            var expected = (head?.Sequence ?? 0) + 1;
            if (record.Sequence != expected)
                return Broken(count, head, expected, $"Expected sequence {expected}, found {record.Sequence} (an entry is missing or reordered).");
            if (record.PreviousHash != (head?.Hash ?? GenesisHash))
                return Broken(count, head, record.Sequence, "The entry does not link to the previous entry's hash.");
            if (ComputeHash(record) != record.Hash)
                return Broken(count, head, record.Sequence, "The entry's content does not match its hash (it was edited).");
            if (anchor is not null && record.Sequence == anchor.Sequence)
            {
                if (record.Hash != anchor.Hash)
                    return Broken(count, head, record.Sequence, "The entry differs from the anchored head (the chain was rewritten).");
                anchorSeen = true;
            }

            head = new SecurityAuditHead(record.ChainId, record.Sequence, record.Hash);
        }

        if (anchor is not null && !anchorSeen)
            return Broken(count, head, anchor.Sequence, $"The chain ends at {head?.Sequence ?? 0}, before the anchored entry {anchor.Sequence} (entries were removed).");

        return new SecurityAuditVerification(true, count, head);
    }

    private static SecurityAuditVerification Broken(long count, SecurityAuditHead? head, long at, string problem)
        => new(false, count, head, at, problem);
}
