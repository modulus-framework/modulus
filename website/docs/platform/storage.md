---
sidebar_position: 6
---

# Storage

Modulus provides file storage abstraction with local and cloud providers.

## Setup

```csharp
services.AddModulusStorage(config);
```

## Providers

| Provider | Package | Configuration |
|----------|---------|---------------|
| **Local** | `Modulus.Platform` | `Storage:BasePath: "./uploads"` |
| **S3** | `Modulus.Storage.S3` | AWS credentials |
| **Azure Blobs** | `Modulus.Storage.AzureBlobs` | Connection string |

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
var stream = await storage.OpenReadAsync("products/123/image.jpg", ct);
```

### Listing Files

```csharp
var files = await storage.ListAsync("products/123/", ct);
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
dotnet add package Cobytelabs.Modulus.Storage.S3
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
dotnet add package Cobytelabs.Modulus.Storage.AzureBlobs
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
