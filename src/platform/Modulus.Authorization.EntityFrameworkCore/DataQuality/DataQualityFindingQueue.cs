namespace Modulus.Authorization.EntityFrameworkCore;

using System.Threading.Channels;
using Modulus.Core.Abstractions.DataQuality;

/// <summary>
/// The sink a request writes its findings to. <see cref="Record"/> only enqueues, so a decision never waits for the database
/// and never fails: when the queue is full the finding is dropped and counted in <see cref="Dropped"/>.
/// <see cref="DataQualityFindingService"/> writes the queued findings to the database.
/// </summary>
public sealed class DataQualityFindingQueue : IDataQualityFindingSink
{
    private readonly Channel<DataQualityFinding> _channel;
    private long _dropped;

    public DataQualityFindingQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _channel = Channel.CreateBounded<DataQualityFinding>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
    }

    /// <summary>Findings dropped because the queue was full, since the process started.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    internal ChannelReader<DataQualityFinding> Reader => _channel.Reader;

    /// <inheritdoc />
    public void Record(DataQualityFinding finding)
    {
        if (!_channel.Writer.TryWrite(finding))
            Interlocked.Increment(ref _dropped);
    }
}
