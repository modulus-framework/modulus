# modulus add-notifications

Adds the notification inbox and per-channel preferences.

```bash
modulus add-notifications
```

## What it adds

- **A store module**, `src/Modules/{App}.Modules.Notifications/{App}.Modules.Notifications.Infrastructure`, with its own `DbContext`, a design-time factory (connection from `NOTIFICATIONS_CONNECTION`) and the module composition root.
- **Host wiring**: `AddModulusNotifications` and `MapModulusNotifications`.
- **The connection string** `Notifications` in `appsettings.json`.

The schema is authored with migrations: run `modulus migrate add InitialCreate --module Notifications` and then `modulus migrate update`. The command is idempotent.
