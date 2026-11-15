namespace Modulus.Messaging.Outbox.EntityFrameworkCore;

/// <summary>One stored outgoing message. Deleted after it has been sent.</summary>
public sealed class OutboxRecord
{
    public Guid MessageId { get; set; }

    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Consumer the message is for, or null for a send to an endpoint.</summary>
    public string? ConsumerKey { get; set; }

    /// <summary>Assembly-qualified name of the message type, used to rebuild it on dispatch.</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>The message as JSON.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The transport headers as a JSON object.</summary>
    public string Headers { get; set; } = "{}";

    /// <summary>UTC. A DateTime (not DateTimeOffset) so every provider can translate the dispatch queries.</summary>
    public DateTime EnqueuedAt { get; set; }

    /// <summary>UTC time the message becomes due. Null when it is sent at once; a row is not dispatched before it.</summary>
    public DateTime? DeliverAt { get; set; }

    /// <summary>Token of the schedule the row belongs to, or null for a message sent at once. Cancelling by it deletes unclaimed rows.</summary>
    public Guid? ScheduleToken { get; set; }

    /// <summary>Set while a dispatcher holds the row; an expired lease lets another dispatcher take it.</summary>
    public Guid? LockToken { get; set; }

    /// <summary>UTC expiry of the lease.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// Set when the message cannot be rebuilt (for example its type no longer exists). Parked rows stay in the
    /// table for an operator to inspect; they are never dropped (RULE-03).
    /// </summary>
    public bool Parked { get; set; }
}
