using ModulusSample.Modules.Settings.Domain.ValueObjects;

namespace ModulusSample.Modules.Settings.Domain.Events;

public sealed record SettingUpdatedDomainEvent(
    SettingId SettingId,
    string Key,
    string OldValue,
    string NewValue,
    string ModifiedBy,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
