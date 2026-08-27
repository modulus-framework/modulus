using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Commands;

public sealed record ApplyCreditNoteCommand(
    Guid CreditNoteId) : ICommand<Result>;