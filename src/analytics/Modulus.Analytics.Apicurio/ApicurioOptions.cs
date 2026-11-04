namespace Modulus.Analytics.Apicurio;

/// <summary>Settings of the Apicurio publisher (section <c>Analytics:Apicurio</c>).</summary>
public sealed class ApicurioOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:Apicurio";

    /// <summary>The registry's base address, for example <c>http://apicurio:8080</c>. Required.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>The registry group that holds the analytics schemas.</summary>
    public string GroupId { get; set; } = "modulus-analytics";

    /// <summary>A bearer token for the registry, if it requires one. Keep it in a secret store.</summary>
    public string? ApiKey { get; set; }

    /// <summary>How long one request may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
