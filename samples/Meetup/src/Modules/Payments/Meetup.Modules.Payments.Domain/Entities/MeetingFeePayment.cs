using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Payments.Domain.Entities;

/// <summary>Payment of a meeting's event fee by an attendee (kgrzybek: Meeting Fee Payment).</summary>
public sealed class MeetingFeePayment : AggregateRoot<Guid>
{
    public string PayerLogin { get; private set; } = string.Empty;
    public Guid MeetingId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public DateTime PaidAt { get; private set; }

    private MeetingFeePayment()
    {
    }

    public static MeetingFeePayment Pay(string payerLogin, Guid meetingId, decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(payerLogin))
        {
            throw new ArgumentException("Payer login is required.", nameof(payerLogin));
        }

        if (amount <= 0)
        {
            throw new ArgumentException("Amount must be positive.", nameof(amount));
        }

        return new MeetingFeePayment
        {
            Id = Guid.NewGuid(),
            PayerLogin = payerLogin.Trim(),
            MeetingId = meetingId,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency,
            PaidAt = DateTime.UtcNow
        };
    }
}
