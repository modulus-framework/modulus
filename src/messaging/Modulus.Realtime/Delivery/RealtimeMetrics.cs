namespace Modulus.Realtime.Delivery;

using System.Diagnostics.Metrics;

/// <summary>Meter <c>Modulus.Realtime</c>: open connections, published, delivered and dropped messages.</summary>
internal sealed class RealtimeMetrics : IDisposable
{
    public const string MeterName = "Modulus.Realtime";

    private readonly Meter _meter;
    private readonly UpDownCounter<long> _connections;
    private readonly Counter<long> _published;
    private readonly Counter<long> _delivered;
    private readonly Counter<long> _dropped;

    public RealtimeMetrics(IMeterFactory meters)
    {
        _meter = meters.Create(MeterName);
        _connections = _meter.CreateUpDownCounter<long>("modulus.realtime.connections", description: "Open realtime connections on this node.");
        _published = _meter.CreateCounter<long>("modulus.realtime.messages.published", description: "Messages this node received for delivery.");
        _delivered = _meter.CreateCounter<long>("modulus.realtime.messages.delivered", description: "Messages queued for a connection.");
        _dropped = _meter.CreateCounter<long>("modulus.realtime.connections.dropped", description: "Connections closed because they fell behind.");
    }

    public void Connected(string transport) => _connections.Add(1, new KeyValuePair<string, object?>("transport", transport));

    public void Disconnected(string transport) => _connections.Add(-1, new KeyValuePair<string, object?>("transport", transport));

    public void Published(string type) => _published.Add(1, new KeyValuePair<string, object?>("type", type));

    public void Delivered(string transport) => _delivered.Add(1, new KeyValuePair<string, object?>("transport", transport));

    public void Dropped(string transport) => _dropped.Add(1, new KeyValuePair<string, object?>("transport", transport));

    public void Dispose() => _meter.Dispose();
}
