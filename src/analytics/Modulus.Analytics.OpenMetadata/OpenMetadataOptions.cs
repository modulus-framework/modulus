namespace Modulus.Analytics.OpenMetadata;

/// <summary>Settings of the OpenMetadata publisher (section <c>Analytics:OpenMetadata</c>).</summary>
public sealed class OpenMetadataOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:OpenMetadata";

    /// <summary>The OpenMetadata server's base address, for example <c>http://openmetadata:8585</c>. Required.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// The database service the warehouse tables are registered under. Create it in OpenMetadata first; the tables go in
    /// <c>{ServiceName}.{DatabaseName}.{SchemaName}</c>, which must exist.
    /// </summary>
    public string ServiceName { get; set; } = "modulus-warehouse";

    /// <summary>The database inside the service.</summary>
    public string DatabaseName { get; set; } = "analytics";

    /// <summary>The schema inside the database.</summary>
    public string SchemaName { get; set; } = "public";

    /// <summary>A bearer token (a JWT) for the server. Keep it in a secret store.</summary>
    public string? ApiKey { get; set; }

    /// <summary>How long one request may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
