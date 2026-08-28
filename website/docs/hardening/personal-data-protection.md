---
sidebar_position: 9
---

# Personal Data Protection

Modulus provides transparent at-rest encryption of designated personal data columns.

## Setup

```csharp
services.AddModulusPersonalDataProtection(config);
```

## Configuration

```json
{
  "PersonalDataProtection": {
    "Enabled": true,
    "Purpose": "Modulus.PersonalData.v1",
    "SearchHashKey": "your-secret-hash-key-out-of-band"
  }
}
```

## Marking Fields

```csharp
public sealed class Customer : AggregateRoot<Guid>
{
    public string Name { get; set; } = default!;

    [ProtectedPersonalData]
    public string Email { get; set; } = default!;

    [ProtectedPersonalData]
    public string PhoneNumber { get; set; } = default!;
}
```

## How It Works

| Phase | Behavior |
|-------|----------|
| **Write** | `Protect()` encrypts the value using ASP.NET Data Protection |
| **Read** | `Unprotect()` decrypts transparently |
| **Storage** | Only ciphertext in the database (`CfDJ8...` format) |
| **Search** | HMAC-SHA256 hash column for equality lookups |

## Search Hash

Since `Protect()` is non-deterministic, encrypted columns can't be queried by equality. Use a companion hash column:

```csharp
public sealed class Customer : AggregateRoot<Guid>
{
    [ProtectedPersonalData]
    public string Email { get; set; } = default!;

    [PersonalDataHash(nameof(Email))]
    public string EmailHash { get; set; } = default!;
}
```

The hash uses a keyed HMAC-SHA256 with `PersonalDataProtection:SearchHashKey`.

## Key Management

- **Key ring** is managed by ASP.NET Data Protection
- Persist the key ring outside the app (file share, DB, Key Vault)
- Restarting without key ring persistence loses decryption ability
- Keep the `Purpose` string stable

## Enabling on Existing Data

1. Enable encryption in config
2. Run a one-off data migration: read plaintext → encrypt → write ciphertext

## See Also

- [Entity Framework](../data/entity-framework) — PII encryption integration
