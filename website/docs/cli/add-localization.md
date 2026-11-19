# modulus add-localization

Adds runtime-editable translations, stored in the database.

```bash
modulus add-localization
```

## What it adds

- **A store module**, `src/Modules/{App}.Modules.Localization/{App}.Modules.Localization.Infrastructure`, with its own `DbContext`, a design-time factory (connection from `LOCALIZATION_CONNECTION`) and the module composition root.
- **Host wiring**: the EF Core translation store (`Synvro.Modulus.Localization.EntityFrameworkCore`), so translations can be edited while the app runs.
- **The connection string** `Localization` in `appsettings.json`.

The schema is authored with migrations: run `modulus migrate add InitialCreate --module Localization` and then `modulus migrate update`. The command is idempotent.
