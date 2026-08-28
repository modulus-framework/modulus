using ProcureFlow.Modules.TradeFinance.Domain.Entities;

namespace ProcureFlow.Modules.TradeFinance.Domain.Repositories;

public interface ILcRepository
{
    Task<LetterOfCredit?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LetterOfCredit?> GetByNumberAsync(Guid tenantId, string lcNumber, CancellationToken ct = default);
    Task<IReadOnlyList<LetterOfCredit>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(LetterOfCredit lc, CancellationToken ct = default);
    Task SaveAsync(LetterOfCredit lc, CancellationToken ct = default);
    Task<bool> ExistsByNumberAsync(Guid tenantId, string lcNumber, CancellationToken ct = default);
}

public interface ITtRepository
{
    Task<TtPayment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TtPayment?> GetByNumberAsync(Guid tenantId, string ttNumber, CancellationToken ct = default);
    Task<IReadOnlyList<TtPayment>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TtPayment tt, CancellationToken ct = default);
    Task SaveAsync(TtPayment tt, CancellationToken ct = default);
    Task<bool> ExistsByNumberAsync(Guid tenantId, string ttNumber, CancellationToken ct = default);
}

public interface ISwiftMessageRepository
{
    Task AddAsync(SwiftMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<SwiftMessage>> GetUnmatchedAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IBankFacilityRepository
{
    Task<BankFacility?> GetByBankAsync(Guid tenantId, Guid bankId, CancellationToken ct = default);
    Task AddAsync(BankFacility facility, CancellationToken ct = default);
    Task SaveAsync(BankFacility facility, CancellationToken ct = default);
}

public interface IPaymentObligationRepository
{
    Task AddAsync(PaymentObligation obligation, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentObligation>> GetUpcomingAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentObligation>> GetOverdueAsync(Guid tenantId, DateOnly asOfDate, CancellationToken ct = default);
}