namespace Modulus.Secrets;

using System.ComponentModel.DataAnnotations;

/// <summary>Settings of the tenant database resolver (section <c>TenantDataStores</c>).</summary>
public sealed class TenantDataStoreOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "TenantDataStores";

    /// <summary>
    /// The first segment of every tenant secret path: <c>{SecretPrefix}/{tenantId}/{storeKey}</c>. It must match the
    /// namespace the runtime identity can read (for example the OpenBao <c>PathPrefix</c>).
    /// </summary>
    [Required, RegularExpression("^[a-z0-9][a-z0-9-]{0,63}$")]
    public string SecretPrefix { get; set; } = "tenant-db";

    /// <summary>The secret field that holds the connection string.</summary>
    [Required, RegularExpression("^[A-Za-z0-9_]{1,64}$")]
    public string ConnectionStringField { get; set; } = "connectionString";

    /// <summary>How long a resolved target is reused before the catalog is read again.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:30:00")]
    public TimeSpan CatalogCacheTtl { get; set; } = TimeSpan.FromSeconds(30);
}
