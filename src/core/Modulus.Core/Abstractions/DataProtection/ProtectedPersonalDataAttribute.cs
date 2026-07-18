namespace Modulus.Core.Abstractions.DataProtection;

/// <summary>
/// Marks a <see cref="string"/> property as personal data that must be encrypted
/// at rest. A module's <c>ModuleDbContext</c> applies a value converter to every
/// marked property automatically (when an <see cref="IPersonalDataProtector"/> is
/// registered), so the column stores ciphertext while the materialised entity holds
/// plaintext — the encryption is transparent to handlers and queries.
/// </summary>
/// <remarks>
/// Encrypted columns cannot be queried by equality (the ciphertext is
/// non-deterministic). For a field you must look up (e.g. an email address), keep an
/// accompanying deterministic hash column populated with
/// <see cref="IPersonalDataProtector.Hash(string)"/> and search on that instead.
/// Only <see cref="string"/> properties are supported; the attribute is ignored on
/// any other type.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ProtectedPersonalDataAttribute : Attribute;
