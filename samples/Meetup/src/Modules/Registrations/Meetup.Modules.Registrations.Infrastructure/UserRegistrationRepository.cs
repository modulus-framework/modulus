using Microsoft.EntityFrameworkCore;
using Meetup.Modules.Registrations.Domain;

namespace Meetup.Modules.Registrations.Infrastructure;

public sealed class UserRegistrationRepository(RegistrationsDbContext db) : IUserRegistrationRepository
{
    public async Task<UserRegistration?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.Registrations.FindAsync([id], ct);

    public async Task<UserRegistration?> FindByLoginAsync(string login, CancellationToken ct)
        => await db.Registrations.SingleOrDefaultAsync(x => x.Login == login, ct);

    public async Task<IReadOnlyList<UserRegistration>> GetAllAsync(CancellationToken ct)
        => await db.Registrations.OrderByDescending(x => x.RegisteredAt).ToListAsync(ct);

    public async Task AddAsync(UserRegistration entity, CancellationToken ct)
        => await db.Registrations.AddAsync(entity, ct);
}
