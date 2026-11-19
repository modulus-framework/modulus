# modulus add-media

Adds the media store: validated uploads with tenant-scoped metadata.

```bash
modulus add-media
```

## What it adds

- **A store module**, `src/Modules/{App}.Modules.Media/{App}.Modules.Media.Infrastructure`, with its own `DbContext`, a design-time factory (connection from `MEDIA_CONNECTION`) and the module composition root.
- **Host wiring**: `AddModulusMedia` and `MapModulusMedia`.
- **The connection string** `Media` in `appsettings.json`.

The schema is authored with migrations: run `modulus migrate add InitialCreate --module Media` and then `modulus migrate update`. The command is idempotent.
