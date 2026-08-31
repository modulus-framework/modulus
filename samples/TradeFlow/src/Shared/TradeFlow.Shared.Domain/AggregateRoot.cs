using Modulus.Core.Abstractions.Domain;

namespace TradeFlow.Shared.Domain;

/// <summary>
/// Bridge between Modulus AggregateRoot and the sample's concurrency-version pattern.
/// </summary>
public abstract class AggregateRoot : Modulus.Core.Abstractions.Domain.AggregateRoot
{
    public long Version { get; protected set; } = 1;

    public void IncrementVersion() => Version++;

    protected void Raise(IDomainEvent domainEvent) => AddDomainEvent(domainEvent);
}
