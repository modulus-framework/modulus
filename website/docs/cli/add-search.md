# modulus add-search

Wires a full-text search server into the API host.

```bash
modulus add-search --provider opensearch
modulus add-search --provider solr
```

| Option | Values | Default |
|---|---|---|
| `--provider` | `opensearch`, `solr` | `opensearch` |

## What it adds

- **The package reference** for the provider (`Synvro.Modulus.Search` and its provider package) in the API host.
- **The registration** in `Configuration/FeatureServicesSetup.cs`: `AddModulusOpenSearch` or `AddModulusSolr`.
- **The settings section** in `appsettings.json` for the chosen server.

The command is idempotent: running it again for the same provider changes nothing.
