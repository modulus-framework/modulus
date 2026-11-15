namespace Modulus.Messaging.Outbox.EntityFrameworkCore;

public sealed class EfOutboxOptions
{
    /// <summary>How long the dispatcher waits when the outbox is empty.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Messages claimed per dispatch round.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>How long a claim lasts. A dispatcher that dies mid-batch loses its claim after this.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>False when the host runs <see cref="OutboxDispatch"/> itself, for example in a dedicated job.</summary>
    public bool RunDispatcher { get; set; } = true;
}
