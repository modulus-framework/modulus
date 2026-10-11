namespace Modulus.Storage;

public sealed class StorageOptions
{
    public string Provider { get; set; } = "Local";
    public string BasePath { get; set; } = "";
    public string? BucketName { get; set; }
    public string? Region { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Endpoint { get; set; }
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Puts every file under the ambient tenant's prefix (<c>tenants/{id:N}/</c>, host: <c>host/</c>)
    /// through <see cref="TenantScopedFileStorage"/>, for every provider. Unset (the default) means on once
    /// <c>AddMultiTenancy</c> is registered and off otherwise; set <c>false</c> to keep one shared file namespace
    /// in a multi-tenant app (existing files keep their paths, so this is a breaking change for upgrades).
    /// </summary>
    public bool? IsolateTenants { get; set; }
}
