using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Payments.Domain.Entities;

/// <summary>
/// Organizer subscription (kgrzybek: Subscription). One subscription covers up
/// to 3 meeting groups; it must stay active for groups to organize meetings.
/// </summary>
public sealed class Subscription : AggregateRoot<Guid>
{
    public string PayerLogin { get; private set; } = string.Empty;
    public DateTime ValidFrom { get; private set; }
    public DateTime ValidTo { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "USD";
    public DateTime PurchasedAt { get; private set; }

    private Subscription()
    {
    }

    public static Subscription Purchase(string payerLogin, decimal price, string currency, int months = 12)
    {
        if (string.IsNullOrWhiteSpace(payerLogin))
        {
            throw new ArgumentException("Payer login is required.", nameof(payerLogin));
        }

        if (price < 0)
        {
            throw new ArgumentException("Price cannot be negative.", nameof(price));
        }

        var from = DateTime.UtcNow;
        return new Subscription
        {
            Id = Guid.NewGuid(),
            PayerLogin = payerLogin.Trim(),
            ValidFrom = from,
            ValidTo = from.AddMonths(months),
            Price = price,
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency,
            PurchasedAt = from
        };
    }

    public void Renew(int months = 12)
    {
        var start = ValidTo > DateTime.UtcNow ? ValidTo : DateTime.UtcNow;
        ValidTo = start.AddMonths(months);
    }

    public bool IsActiveAt(DateTime at) => ValidFrom <= at && at < ValidTo;

    /// <summary>
    /// Attaches a domain event so <c>ModuleDbContext</c> enqueues it into the
    /// module outbox transactionally. Called by the Application handler with
    /// the module's integration event (which implements <c>IDomainEvent</c>).
    /// </summary>
    public void AddIntegrationEvent(IDomainEvent @event) => AddDomainEvent(@event);
}
