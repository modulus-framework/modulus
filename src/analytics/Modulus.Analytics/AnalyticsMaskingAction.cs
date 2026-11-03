namespace Modulus.Analytics;

/// <summary>What happens to a field's value on its way to an analytics provider.</summary>
public enum AnalyticsMaskingAction
{
    /// <summary>The value is sent unchanged.</summary>
    Keep = 0,

    /// <summary>The value is replaced by a stable keyed hash, so it can still be grouped and joined but not read.</summary>
    Hash = 1,

    /// <summary>The field is removed and never leaves the system.</summary>
    Drop = 2,
}
