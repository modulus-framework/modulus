namespace Meetup.Modules.Registrations.Application;

/// <summary>
/// Unit of work for the Registrations module (bound to RegistrationsDbContext).
/// </summary>
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
