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
}
