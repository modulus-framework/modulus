namespace Meetup.Modules.Administration.Application;

/// <summary>Unit of work for the Administration module (bound to AdministrationDbContext).</summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
