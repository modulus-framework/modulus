---
sidebar_position: 6
---

# Storage

Modulus provides file storage abstraction with local and cloud providers.

## Setup

```csharp
services.AddFileStorage(config);   // binds the Storage section
```

## Providers

| Provider | Package | Configuration |
|----------|---------|---------------|
| **Local** | `Modulus.Platform` | `Storage:Provider: "Local"`, `Storage:BasePath: "./uploads"` |
| **S3** | `Modulus.Storage.S3` | `services.AddS3FileStorage(config)`; `Storage:BucketName`, `Region`, `Endpoint`, `ForcePathStyle` (for MinIO and Ceph) |
| **Azure Blobs** | `Modulus.Storage.AzureBlobs` | `services.AddAzureBlobFileStorage(config)`; `Storage:ConnectionString` |

Each provider reads the `Storage` section. Register one provider per application. Use `IsolateTenants` (default: on once multi-tenancy is registered) to keep each company's files under its own prefix.

`Storage:MaxUploadBytes` limits uploads through `UploadAsync` (default 100 MB). Presigned uploads are not limited by it; S3 presigned POST uploads take their own size limit.

## Usage

### IFileStorage

```csharp
public sealed class UploadProductImageHandler(IFileStorage storage)
    : ICommandHandler<UploadProductImage, Unit>
{
    public async Task<Unit> HandleAsync(UploadProductImage command, CancellationToken ct)
    {
        var path = $"products/{command.ProductId}/image.jpg";

        await storage.UploadAsync(path, command.ImageStream, "image/jpeg", ct);

        return Unit.Value;
    }
}
```

### Reading Files

```csharp
await using var stream = await storage.DownloadAsync("products/123/image.jpg", ct);
```

### Existence + Deletion

```csharp
if (await storage.ExistsAsync("products/123/image.jpg", ct))
    await storage.DeleteAsync("products/123/image.jpg", ct);
```

### Presigned URLs

Direct client-to-storage transfers without server-side relay:

```csharp
// Download (GET) — valid for the given duration, no credentials needed
var url = await storage.GetPresignedUrlAsync("products/123/image.jpg", TimeSpan.FromHours(1), ct);

// Upload (PUT) — optionally constrain the content type
var uploadUrl = await storage.GetPresignedUploadUrlAsync(
    "products/123/image.jpg", TimeSpan.FromMinutes(15), "image/jpeg", ct);
```

## Local Storage

```json
{
  "Storage": {
    "Provider": "local",
    "BasePath": "./uploads",
    "MaxFileSizeBytes": 10485760
  }
}
```

## S3 Storage

```bash
dotnet add package Synvro.Modulus.Storage.S3
```

```json
{
  "S3": {
    "BucketName": "my-bucket",
    "Region": "us-east-1"
  }
}
```

## Azure Blob Storage

```bash
dotnet add package Synvro.Modulus.Storage.AzureBlobs
```

```json
{
  "AzureBlobs": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
    "ContainerName": "uploads"
  }
}
```

## See Also

- [Platform Overview](overview) — Other platform services
