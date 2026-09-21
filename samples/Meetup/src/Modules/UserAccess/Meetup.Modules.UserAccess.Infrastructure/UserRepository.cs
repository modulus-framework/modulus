using Microsoft.EntityFrameworkCore;
using Meetup.Modules.UserAccess.Domain;

namespace Meetup.Modules.UserAccess.Infrastructure;

public sealed class UserRepository(UserAccessDbContext db) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.Users.FindAsync([id], ct);

    public async Task<User?> FindByLoginAsync(string login, CancellationToken ct)
        => await db.Users.SingleOrDefaultAsync(x => x.Login == login, ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct)
        => await db.Users.OrderBy(x => x.Login).ToListAsync(ct);

    public async Task AddAsync(User entity, CancellationToken ct)
        => await db.Users.AddAsync(entity, ct);
}
