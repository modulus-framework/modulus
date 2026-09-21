namespace Meetup.Modules.Meetings.Application;

/// <summary>Unit of work for the Meetings module (bound to MeetingsDbContext).</summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
