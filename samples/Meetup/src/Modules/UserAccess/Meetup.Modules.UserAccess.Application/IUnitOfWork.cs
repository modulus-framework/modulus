namespace Meetup.Modules.UserAccess.Application;

/// <summary>Unit of work for the UserAccess module (bound to UserAccessDbContext).</summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
