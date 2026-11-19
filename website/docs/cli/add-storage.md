# modulus add-storage

Wires a cloud file-storage provider into the API host.

```bash
modulus add-storage --provider s3
modulus add-storage --provider azure
modulus add-storage --provider gcs
```

| Option | Values | Default |
|---|---|---|
| `--provider` | `s3`, `azure`, `gcs` | `s3` |

## What it adds

- **The package reference** for the provider: `Synvro.Modulus.Storage.S3`, `Synvro.Modulus.Storage.AzureBlobs` or `Synvro.Modulus.Storage.GoogleCloud`.
- **The registration** in `Configuration/FeatureServicesSetup.cs`: `AddS3FileStorage`, `AddAzureBlobFileStorage` or `AddGoogleCloudFileStorage`.
- **The settings section** in `appsettings.json`.

Storage uses the local file provider until one of these is added. The command is idempotent.
