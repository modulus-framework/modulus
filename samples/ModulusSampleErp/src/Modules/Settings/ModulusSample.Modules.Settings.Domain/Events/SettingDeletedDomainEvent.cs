using ModulusSample.Modules.Settings.Domain.ValueObjects;

namespace ModulusSample.Modules.Settings.Domain.Events;

public sealed record SettingDeletedDomainEvent(
    SettingId SettingId,
    string Key,
    string Value,
    string DeletedBy,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase;
