using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.CreditNotes.Dtos;

namespace ModulusSample.Modules.Billing.Application.CreditNotes.Queries;

public sealed record GetCreditNoteByIdQuery(Guid CreditNoteId) : IQuery<CreditNoteDto?>;