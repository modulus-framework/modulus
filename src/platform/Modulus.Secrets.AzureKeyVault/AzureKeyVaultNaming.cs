namespace Modulus.Secrets.AzureKeyVault;

using System.Text.Json;
using Modulus.Core.Abstractions.Secrets;

/// <summary>
/// The mapping between a Modulus secret reference and an Azure Key Vault secret. A Key Vault secret name allows only letters,
/// digits and hyphens, so the reference's slashes become double hyphens. A Key Vault secret holds one string, so a multi-field
/// Modulus secret is stored as a JSON object of fields; any other string is read as the single field <c>value</c>.
/// </summary>
public static class AzureKeyVaultNaming
{
    /// <summary>Key Vault allows names of up to 127 characters.</summary>
    public const int MaxSecretNameLength = 127;

    /// <summary>The field name of a value that is not a JSON object of fields.</summary>
    public const string SingleValueField = "value";

    // Version numbers are whole seconds since this instant. Key Vault versions are GUIDs, so the framework's integer version is
    // derived from the last update time. Counting from 2026 keeps the value within int range until about 2094.
    private static readonly DateTimeOffset VersionEpoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The Key Vault secret name for a reference, for example <c>tenant-db--3f2c--primary</c>.</summary>
    public static string ToSecretName(SecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var name = reference.Path.Replace("/", "--", StringComparison.Ordinal);
        if (name.Length > MaxSecretNameLength)
            throw new ArgumentException($"The secret name would be {name.Length} characters; Key Vault allows {MaxSecretNameLength}.", nameof(reference));
        return name;
    }

    /// <summary>Reads the fields of a Key Vault secret value.</summary>
    public static IReadOnlyDictionary<string, string> ReadFields(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.TrimStart().StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind == JsonValueKind.Object && AllPrimitive(document.RootElement))
                {
                    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() ?? string.Empty
                            : property.Value.GetRawText();
                    }

                    return fields;
                }
            }
            catch (JsonException)
            {
                // Not JSON: the value is a single field below.
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal) { [SingleValueField] = raw };
    }

    /// <summary>The integer version of a Key Vault secret version, from its last update time (or creation time).</summary>
    public static int ToVersionNumber(DateTimeOffset? updatedOn, DateTimeOffset? createdOn)
    {
        var moment = updatedOn ?? createdOn ?? VersionEpoch;
        var seconds = (long)(moment - VersionEpoch).TotalSeconds;
        return (int)Math.Clamp(seconds, 0, int.MaxValue);
    }

    private static bool AllPrimitive(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            var kind = property.Value.ValueKind;
            if (kind is JsonValueKind.Object or JsonValueKind.Array)
                return false;
        }

        return true;
    }
}
