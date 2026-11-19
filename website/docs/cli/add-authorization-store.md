# modulus add-authorization-store

Moves an existing app's authorization store from the framework's default database context to the application's own.

```bash
modulus add-authorization-store
```

## Why

By default the framework creates the authorization tables itself. This command gives the app its own context, so the schema is authored with migrations like every other module. Production refuses to create the store without migrations.

## What it adds

- **A store module**, `src/Modules/{App}.Modules.Authorization/{App}.Modules.Authorization.Infrastructure`, with `AppAuthorizationDbContext`, its design-time factory and `AuthorizationStoreRegistration`.
- **Host wiring** that uses the new context in place of the default one.

The command is idempotent. The new `InitialCreate` migration must not be applied on top of a database that already has the schema: for an existing database, add the migration as a baseline instead.
