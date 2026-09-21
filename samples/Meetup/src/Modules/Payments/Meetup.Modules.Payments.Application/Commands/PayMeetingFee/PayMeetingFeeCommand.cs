using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Payments.Application.Commands.PayMeetingFee;

/// <summary>Pays the event fee for a meeting.</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record PayMeetingFeeCommand(string PayerLogin, Guid MeetingId, decimal Amount, string Currency)
    : ICommand<Guid>;
