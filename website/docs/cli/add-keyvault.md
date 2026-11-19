# modulus add-keyvault

Reads application secrets from Azure Key Vault.

```bash
modulus add-keyvault
```

## What it adds

- **The package reference** `Synvro.Modulus.Secrets.AzureKeyVault` in the API host.
- **The registration** in `Configuration/FeatureServicesSetup.cs`: `AddAzureKeyVaultSecretStore`.
- **The settings section** in `appsettings.json` (the vault URI).

The provider is read-only: the framework reads secrets from the vault and does not write them. It authenticates with `DefaultAzureCredential`, so no secret is stored in the app. The command is idempotent.
