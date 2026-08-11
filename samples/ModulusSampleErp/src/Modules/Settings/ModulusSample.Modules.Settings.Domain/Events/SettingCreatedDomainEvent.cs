using ModulusSample.Modules.Settings.Domain.ValueObjects;

namespace ModulusSample.Modules.Settings.Domain.Events;

public sealed record SettingCreatedDomainEvent(
    SettingId SettingId,
    string Key,
    string Category,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
