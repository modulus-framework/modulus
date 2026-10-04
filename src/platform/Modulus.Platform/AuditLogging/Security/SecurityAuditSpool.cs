namespace Modulus.AuditLogging.Security;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Modulus.Core.Abstractions;

/// <summary>
/// The write-ahead journal behind <see cref="SecurityAuditOptions.SpoolFile"/>: every event is appended (and flushed to
/// disk) before it is queued, and acknowledged once the store holds it, so an event recorded just before a crash, or one
/// the store kept refusing, is appended again at the next start. Delivery is therefore at least once: an event stored in
/// the instant before its acknowledgement was written comes back as a second entry. The file is truncated whenever
/// nothing is outstanding. One file per process: replicas must not share it.
/// </summary>
internal sealed class SecurityAuditSpool : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly FileStream _file;
    private readonly HashSet<Guid> _outstanding = [];

    public SecurityAuditSpool(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        Pending = Load();
        foreach (var (id, _) in Pending)
            _outstanding.Add(id);
        _file.Seek(0, SeekOrigin.End);
        if (Pending.Count == 0)
            _file.SetLength(0);
    }

    /// <summary>The events journaled by an earlier run and never acknowledged, in recording order.</summary>
    public IReadOnlyList<(Guid Id, SecurityAuditEvent Event)> Pending { get; }

    /// <summary>Journals <paramref name="auditEvent"/> under <paramref name="id"/>; returns false when the disk write failed.</summary>
    public bool TryAppend(Guid id, SecurityAuditEvent auditEvent)
    {
        lock (_gate)
        {
            try
            {
                Write(new SpoolLine { Id = id, Event = auditEvent });
                _outstanding.Add(id);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    /// <summary>Marks <paramref name="id"/> as stored; truncates the file once nothing is outstanding.</summary>
    public void Acknowledge(Guid id)
    {
        lock (_gate)
        {
            if (!_outstanding.Remove(id))
                return;
            try
            {
                if (_outstanding.Count == 0)
                {
                    _file.SetLength(0);
                    _file.Flush(flushToDisk: true);
                }
                else
                {
                    Write(new SpoolLine { Ack = id });
                }
            }
            catch (IOException)
            {
                // A lost acknowledgement only means the event is appended once more at the next start.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _file.Dispose();
    }

    private void Write(SpoolLine line)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(line, s_json) + "\n");
        _file.Write(bytes);
        _file.Flush(flushToDisk: true);
    }

    private List<(Guid, SecurityAuditEvent)> Load()
    {
        var events = new List<(Guid Id, SecurityAuditEvent Event)>();
        var acknowledged = new HashSet<Guid>();
        _file.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(_file, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        while (reader.ReadLine() is { } text)
        {
            SpoolLine? line;
            try
            {
                line = JsonSerializer.Deserialize<SpoolLine>(text, s_json);
            }
            catch (JsonException)
            {
                continue; // a line cut short by a crash
            }

            if (line?.Ack is { } ack)
                acknowledged.Add(ack);
            else if (line is { Id: { } id, Event: { } auditEvent })
                events.Add((id, auditEvent));
        }

        return events.Where(e => !acknowledged.Contains(e.Id)).ToList();
    }

    private sealed class SpoolLine
    {
        public Guid? Id { get; init; }

        public SecurityAuditEvent? Event { get; init; }

        public Guid? Ack { get; init; }
    }
}
