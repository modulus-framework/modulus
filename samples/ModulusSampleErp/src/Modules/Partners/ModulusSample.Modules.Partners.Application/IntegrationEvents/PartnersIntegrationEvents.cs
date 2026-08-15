namespace ModulusSample.Modules.Partners.Application.IntegrationEvents;

public sealed record PartnerCreatedIntegrationEvent(Guid PartnerId, string Name, string Type, DateTime CreatedAtUtc);
public sealed record PartnerUpdatedIntegrationEvent(Guid PartnerId, string Name, string Type, DateTime UpdatedAtUtc);
public sealed record PartnerActivatedIntegrationEvent(Guid PartnerId, string Name, DateTime ActivatedAtUtc);
public sealed record PartnerDeactivatedIntegrationEvent(Guid PartnerId, string Name, string Reason, DateTime DeactivatedAtUtc);
public sealed record PartnerContactAddedIntegrationEvent(Guid PartnerId, string Name, Guid ContactId, string ContactEmail, DateTime AddedAtUtc);
public sealed record SupplierCreatedIntegrationEvent(Guid PartnerId, string Name, DateTime CreatedAtUtc);
public sealed record SupplierUpdatedIntegrationEvent(Guid PartnerId, string Name, DateTime UpdatedAtUtc);
public sealed record CustomerCreatedIntegrationEvent(Guid PartnerId, string Name, DateTime CreatedAtUtc);
public sealed record CustomerUpdatedIntegrationEvent(Guid PartnerId, string Name, DateTime UpdatedAtUtc);
public sealed record CustomerCreditLimitChangedIntegrationEvent(Guid CustomerId, decimal OldLimit, decimal NewLimit, DateTime ChangedAtUtc);