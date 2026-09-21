using Meetup.Modules.Payments.Domain.Entities;

namespace Meetup.Modules.Payments.Domain.Repositories;

/// <summary>Persistence abstractions (implemented in Infrastructure with EF Core).</summary>
public interface ISubscriptionRepository
{
    Task<Subscription?> FindLatestByPayerAsync(string payerLogin, CancellationToken ct);
    Task<IReadOnlyList<Subscription>> FindByPayerAsync(string? payerLogin, CancellationToken ct);
    Task AddAsync(Subscription entity, CancellationToken ct);
}
