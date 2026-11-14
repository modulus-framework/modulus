namespace Modulus.Storage;

/// <summary>
/// Google Cloud Storage settings (<c>Storage:GoogleCloud</c>). The bucket, the emulator endpoint and the base path come from the
/// shared <c>Storage</c> section (<c>BucketName</c>, <c>Endpoint</c>).
/// </summary>
public sealed class GoogleCloudStorageOptions
{
    /// <summary>
    /// The service-account key file (JSON) used to sign presigned URLs. Without it, presigned URLs are refused: Google signs them
    /// with a service-account key only. Keep the file out of the repository; the path is a secret reference, not the key.
    /// </summary>
    public string? CredentialsPath { get; set; }
}
