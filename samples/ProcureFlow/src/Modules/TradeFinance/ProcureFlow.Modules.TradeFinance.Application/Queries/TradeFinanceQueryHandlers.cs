using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.TradeFinance.Application.Dtos;
using ProcureFlow.Modules.TradeFinance.Application.Queries;
using ProcureFlow.Modules.TradeFinance.Domain.Entities;
using ProcureFlow.Modules.TradeFinance.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.TradeFinance.Application.Queries;

public sealed class GetLcHandler(ILcRepository repository) : IQueryHandler<GetLcQuery, Result<LetterOfCreditResponse>>
{
    public async Task<Result<LetterOfCreditResponse>> HandleAsync(GetLcQuery query, CancellationToken ct)
    {
        LetterOfCredit? lc = await repository.GetByIdAsync(query.LcId, ct);
        return lc is null
            ? Result.Failure<LetterOfCreditResponse>(Error.NotFound("Lc.NotFound", "LC not found"))
            : Result.Success(TradeFinanceResponseFactory.ToLcResponse(lc));
    }
}

public sealed class ListLcsHandler(
    ILcRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListLcsQuery, Result<IReadOnlyList<LetterOfCreditResponse>>>
{
    public async Task<Result<IReadOnlyList<LetterOfCreditResponse>>> HandleAsync(ListLcsQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<LetterOfCredit> lcs = await repository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<LetterOfCreditResponse>>(lcs.Select(TradeFinanceResponseFactory.ToLcResponse).ToArray());
    }
}

public sealed class GetTtHandler(ITtRepository repository) : IQueryHandler<GetTtQuery, Result<TtPaymentResponse>>
{
    public async Task<Result<TtPaymentResponse>> HandleAsync(GetTtQuery query, CancellationToken ct)
    {
        TtPayment? tt = await repository.GetByIdAsync(query.TtId, ct);
        return tt is null
            ? Result.Failure<TtPaymentResponse>(Error.NotFound("Tt.NotFound", "TT not found"))
            : Result.Success(TradeFinanceResponseFactory.ToTtResponse(tt));
    }
}

public sealed class ListTtsHandler(
    ITtRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<ListTtsQuery, Result<IReadOnlyList<TtPaymentResponse>>>
{
    public async Task<Result<IReadOnlyList<TtPaymentResponse>>> HandleAsync(ListTtsQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<TtPayment> tts = await repository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<TtPaymentResponse>>(tts.Select(TradeFinanceResponseFactory.ToTtResponse).ToArray());
    }
}

public sealed class GetUnmatchedSwiftHandler(
    ISwiftMessageRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetUnmatchedSwiftQuery, Result<IReadOnlyList<SwiftMessageResponse>>>
{
    public async Task<Result<IReadOnlyList<SwiftMessageResponse>>> HandleAsync(GetUnmatchedSwiftQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<SwiftMessage> messages = await repository.GetUnmatchedAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<SwiftMessageResponse>>(messages.Select(m => new SwiftMessageResponse(
            m.Id, m.TenantId, m.MtType, m.Reference, m.Direction, m.LinkedLcId, m.LinkedTtId,
            m.ContentRef, m.IsMatched)).ToArray());
    }
}

public sealed class GetObligationsHandler(
    IPaymentObligationRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetObligationsQuery, Result<IReadOnlyList<PaymentObligationResponse>>>
{
    public async Task<Result<IReadOnlyList<PaymentObligationResponse>>> HandleAsync(GetObligationsQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        IReadOnlyList<PaymentObligation> obligations = await repository.GetUpcomingAsync(tenantId, query.From, query.To, ct);
        return Result.Success<IReadOnlyList<PaymentObligationResponse>>(obligations.Select(o => new PaymentObligationResponse(
            o.Id, o.TenantId, o.Type, o.SourceId, o.SourceNumber, o.DueDate, o.Amount, o.Currency,
            o.Status, o.NotifiedT7, o.NotifiedT3)).ToArray());
    }
}

public sealed class GetFacilityHandler(
    IBankFacilityRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetFacilityQuery, Result<BankFacilityResponse>>
{
    public async Task<Result<BankFacilityResponse>> HandleAsync(GetFacilityQuery query, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        BankFacility? facility = await repository.GetByBankAsync(tenantId, query.BankId, ct);
        return facility is null
            ? Result.Failure<BankFacilityResponse>(Error.NotFound("Facility.NotFound", "No facility registered for bank"))
            : Result.Success(new BankFacilityResponse(facility.Id, facility.TenantId, facility.BankId,
                facility.LimitAmount, facility.Currency, facility.Outstanding, facility.Available));
    }
}