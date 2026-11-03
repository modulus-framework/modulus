namespace Modulus.Analytics;

/// <summary>
/// Moves a record's values from one dataset version to the next, so rows captured or queued under an old contract still reach
/// providers in the current shape. One upcaster covers one step (<see cref="FromVersion"/> to <see cref="FromVersion"/> + 1);
/// the dispatcher chains steps.
/// </summary>
public interface IAnalyticsRecordUpcaster
{
    /// <summary>The dataset this step belongs to.</summary>
    string Dataset { get; }

    /// <summary>The version this step reads. The result is shaped for <c>FromVersion + 1</c>.</summary>
    int FromVersion { get; }

    /// <summary>Returns the values reshaped for the next version. It must not change <paramref name="values"/>.</summary>
    /// <param name="values">The values at <see cref="FromVersion"/>.</param>
    IReadOnlyDictionary<string, object?> Upcast(IReadOnlyDictionary<string, object?> values);
}
