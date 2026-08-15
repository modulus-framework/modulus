namespace ModulusSample.Modules.Partners.Domain.Events;

public sealed record PartnerCreatedDomainEvent(Guid EventId, Guid PartnerId, string Name, string Type, DateTime CreatedAtUtc);
public sealed record PartnerUpdatedDomainEvent(Guid EventId, Guid PartnerId, string Name, string Type, DateTime UpdatedAtUtc);
public sealed record PartnerActivatedDomainEvent(Guid EventId, Guid PartnerId, string Name, DateTime ActivatedAtUtc);
public sealed record PartnerDeactivatedDomainEvent(Guid EventId, Guid PartnerId, string Name, string Reason, DateTime DeactivatedAtUtc);
public sealed record PartnerTypeChangedDomainEvent(Guid EventId, Guid PartnerId, string Name, string OldType, string NewType, DateTime ChangedAtUtc);
public sealed record PartnerContactAddedDomainEvent(Guid EventId, Guid PartnerId, string Name, Guid ContactId, string ContactEmail, DateTime AddedAtUtc);
public sealed record PartnerContactUpdatedDomainEvent(Guid EventId, Guid PartnerId, string Name, Guid ContactId, string ContactEmail, DateTime UpdatedAtUtc);
public sealed record PartnerContactRemovedDomainEvent(Guid EventId, Guid PartnerId, string Name, Guid ContactId, DateTime RemovedAtUtc);
public sealed record PartnerAddressAddedDomainEvent(Guid EventId, Guid PartnerId, string Name, Guid AddressId, string AddressType, DateTime AddedAtUtc);
public sealed record PartnerAddressUpdatedDomainEvent(Guid EventId, Guid PartnerId, string Name, Guid AddressId, string AddressType, DateTime UpdatedAtUtc);
public sealed record PartnerAddressRemovedDomainEvent(Guid EventId, Guid PartnerId, string Name, Guid AddressId, DateTime RemovedAtUtc);
public sealed record PartnerCreditLimitChangedDomainEvent(Guid EventId, Guid PartnerId, string Name, decimal OldLimit, decimal NewLimit, DateTime ChangedAtUtc);
public sealed record PartnerTaxIdChangedDomainEvent(Guid EventId, Guid PartnerId, string Name, string OldTaxId, string NewTaxId, DateTime ChangedAtUtc);