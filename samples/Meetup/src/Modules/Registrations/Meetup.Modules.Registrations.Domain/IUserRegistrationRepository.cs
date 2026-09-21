namespace Meetup.Modules.Registrations.Domain;

/// <summary>Persistence abstraction (implemented in Infrastructure with EF Core).</summary>
public interface IUserRegistrationRepository
{
    Task<UserRegistration?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<UserRegistration?> FindByLoginAsync(string login, CancellationToken ct);
    Task<IReadOnlyList<UserRegistration>> GetAllAsync(CancellationToken ct);
    Task AddAsync(UserRegistration entity, CancellationToken ct);
}
