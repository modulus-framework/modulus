namespace Modulus.Analytics;

/// <summary>The logical type of a dataset field, independent of any provider's column types.</summary>
public enum AnalyticsFieldType
{
    /// <summary>UTF-8 text.</summary>
    String = 0,

    /// <summary>64-bit signed integer.</summary>
    Int64 = 1,

    /// <summary>Exact decimal number.</summary>
    Decimal = 2,

    /// <summary>Double-precision floating point number.</summary>
    Double = 3,

    /// <summary>True or false.</summary>
    Boolean = 4,

    /// <summary>A point in time, stored in UTC.</summary>
    Timestamp = 5,

    /// <summary>A calendar date without a time.</summary>
    Date = 6,

    /// <summary>A GUID.</summary>
    Guid = 7,
}
