namespace Modulus.Analytics.ClickHouse;

using System.Data.Common;

/// <summary>Decides whether a ClickHouse failure is worth retrying.</summary>
internal static class ClickHouseErrors
{
    // Server error codes (see system.errors) that repeat on every attempt: a bad request, an unknown object, or refused credentials.
    private static readonly HashSet<int> Permanent =
    [
        27,  // CANNOT_PARSE_INPUT_ASSERTION_FAILED
        38,  // CANNOT_PARSE_DATE
        41,  // CANNOT_PARSE_DATETIME
        43,  // ILLEGAL_TYPE_OF_ARGUMENT
        47,  // UNKNOWN_IDENTIFIER
        53,  // TYPE_MISMATCH
        60,  // UNKNOWN_TABLE
        62,  // SYNTAX_ERROR
        81,  // UNKNOWN_DATABASE
        192, // UNKNOWN_USER
        193, // WRONG_PASSWORD
        497, // ACCESS_DENIED
        516, // AUTHENTICATION_FAILED
    ];

    internal static AnalyticsSinkException Translate(string action, Exception exception) => exception switch
    {
        AnalyticsSinkException known => known,
        DbException db when Permanent.Contains(db.ErrorCode) =>
            new AnalyticsSinkException($"ClickHouse refused to {action}: {db.Message}", isTransient: false, exception),
        ArgumentException or InvalidCastException or FormatException or OverflowException =>
            new AnalyticsSinkException($"A value could not be sent to ClickHouse while trying to {action}: {exception.Message}", isTransient: false, exception),
        _ => new AnalyticsSinkException($"ClickHouse failed to {action}: {exception.Message}", isTransient: true, exception),
    };
}
