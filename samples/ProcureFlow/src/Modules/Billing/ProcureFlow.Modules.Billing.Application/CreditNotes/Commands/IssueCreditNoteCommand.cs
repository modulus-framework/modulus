using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Commands;

public sealed record IssueCreditNoteCommand(
    Guid CreditNoteId) : ICommand<Result>;