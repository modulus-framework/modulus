namespace ProcureFlow.Modules.Finance.Application;

public interface IFinanceUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(Guid userId, CancellationToken ct = default);
}