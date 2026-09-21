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
    "Purpose": "Modulus.PersonalData.Protector.v1",
    "SearchHashKey": "your-secret-hash-key-out-of-band",
    "KeyRingDirectory": "/var/keys/myapp",
    "ApplicationName": "Modulus"
  }
}
```

`KeyRingDirectory` is required in Production — without key-ring persistence
every restart/replica mints a fresh ring and old ciphertext becomes
undecryptable. `ApplicationName` isolates the ring when several apps share a
folder.

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

Since `Protect()` is non-deterministic, encrypted columns can't be queried by
equality. Use a companion hash column populated via
`IPersonalDataProtector.Hash` (deterministic keyed HMAC-SHA256):

```csharp
public sealed class Customer : AggregateRoot<Guid>
{
    [ProtectedPersonalData]
    public string Email { get; set; } = default!;

    // Companion column for equality search, populated in code via
    // protector.Hash(email). There is no [PersonalDataHash] attribute.
    public string EmailHash { get; set; } = default!;
}
```

The hash uses a keyed HMAC-SHA256 with `PersonalDataProtection:SearchHashKey`
(supplied out-of-band, never committed).

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
