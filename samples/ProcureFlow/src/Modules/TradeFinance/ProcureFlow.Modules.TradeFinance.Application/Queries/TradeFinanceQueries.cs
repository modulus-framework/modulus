using ProcureFlow.Modules.TradeFinance.Application.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.TradeFinance.Application.Queries;

public sealed record GetLcQuery(Guid LcId) : Modulus.Mediator.Abstractions.IQuery<Result<LetterOfCreditResponse>>;

public sealed record ListLcsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<LetterOfCreditResponse>>>;

public sealed record GetTtQuery(Guid TtId) : Modulus.Mediator.Abstractions.IQuery<Result<TtPaymentResponse>>;

public sealed record ListTtsQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<TtPaymentResponse>>>;

public sealed record GetUnmatchedSwiftQuery : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<SwiftMessageResponse>>>;

public sealed record GetObligationsQuery(
    DateOnly From,
    DateOnly To) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<PaymentObligationResponse>>>;

public sealed record GetFacilityQuery(
    Guid BankId) : Modulus.Mediator.Abstractions.IQuery<Result<BankFacilityResponse>>;