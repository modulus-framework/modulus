namespace Meetup.Modules.Payments.Application;

/// <summary>Unit of work for the Payments module (bound to PaymentsDbContext).</summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
