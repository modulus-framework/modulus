namespace Meetup.Modules.UserAccess.Domain;

/// <summary>Persistence abstraction (implemented in Infrastructure with EF Core).</summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<User?> FindByLoginAsync(string login, CancellationToken ct);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct);
    Task AddAsync(User entity, CancellationToken ct);
}
