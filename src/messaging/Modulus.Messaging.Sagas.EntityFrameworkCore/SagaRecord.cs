namespace Modulus.Messaging.Sagas.EntityFrameworkCore;

/// <summary>One stored saga instance: its JSON state and the version the optimistic check compares.</summary>
public sealed class SagaRecord
{
    /// <summary>The saga class's full name, so one table holds the instances of several sagas.</summary>
    public string SagaType { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; }

    /// <summary>Incremented by every save. A write succeeds only while the stored version is the one the writer read.</summary>
    public long Version { get; set; }

    /// <summary>The instance as JSON.</summary>
    public string Data { get; set; } = "{}";
}
