namespace Modulus.Tenancy.Lifecycle.Certificates;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The deletion certificate: a JSON payload listing what each dataset did (deleted, retained, pending deletion, global), the
/// record counts, when backups expire, and an HMAC-SHA256 signature over the exact payload text. The payload is stored verbatim,
/// so verification does not depend on re-serializing it.
/// </summary>
internal static class DeletionCertificates
{
    public const int Version = 1;

    // Enums by name, so an auditor reads "Deleted", not 0.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Builds the payload for an operation's results.</summary>
    public static string BuildPayload(LifecycleOperation operation, DateTimeOffset completedAt, DateTimeOffset backupsExpireAt, string keyId)
    {
        var body = new Body(
            Version,
            operation.Id,
            operation.TenantId,
            operation.Kind,
            operation.RequestedBy,
            operation.RequestedAt,
            completedAt,
            backupsExpireAt,
            keyId,
            operation.Outcomes.ToList());
        return JsonSerializer.Serialize(body, Json);
    }

    /// <summary>Signs a payload (base64 HMAC-SHA256).</summary>
    public static string Sign(string payload, byte[] key) =>
        Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));

    /// <summary>Checks a signature in constant time.</summary>
    public static bool Verify(string payload, string signature, byte[] key)
    {
        byte[] given;
        try
        {
            given = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <summary>The signed body. Its JSON is what is signed.</summary>
    internal sealed record Body(
        int Version,
        Guid OperationId,
        Guid TenantId,
        OperationKind Kind,
        string RequestedBy,
        DateTimeOffset RequestedAt,
        DateTimeOffset CompletedAt,
        DateTimeOffset BackupsExpireAt,
        string KeyId,
        IReadOnlyList<DatasetOutcome> Datasets);
}
