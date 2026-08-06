using System.Text.Json.Serialization;

namespace ModulusSample.Modules.Identity.Domain.ValueObjects;

public sealed record DeviceInfo
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string Browser { get; init; }
    public string? BrowserVersion { get; init; }
    public string Os { get; init; }
    public string? OsVersion { get; init; }
    public string DeviceType { get; init; }

    public DeviceInfo() : this("Unknown", null, "Unknown", null, "Unknown") { }

    [JsonConstructor]
    public DeviceInfo(string browser, string? browserVersion, string os, string? osVersion, string deviceType)
    {
        Browser = browser;
        BrowserVersion = browserVersion;
        Os = os;
        OsVersion = osVersion;
        DeviceType = deviceType;
    }

    public static DeviceInfo Create(
        string browser,
        string? browserVersion,
        string os,
        string? osVersion,
        string deviceType)
    {
        return new DeviceInfo(browser, browserVersion, os, osVersion, deviceType);
    }

    public static DeviceInfo Empty => new()
    {
        Browser = "Unknown",
        Os = "Unknown",
        DeviceType = "Unknown"
    };

    /// <summary>
    /// True when device info was never populated (created without a User-Agent).
    /// </summary>
    public bool IsUnknown =>
        string.IsNullOrEmpty(Browser) || Browser == "Unknown" ||
        string.IsNullOrEmpty(Os) || Os == "Unknown";

    public string ToJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, JsonOptions);
    }

    public static DeviceInfo FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Empty;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<DeviceInfo>(json, JsonOptions) ?? Empty;
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.WriteLine($"Failed to deserialize DeviceInfo: {ex.Message}");
            return Empty;
        }
    }
}
