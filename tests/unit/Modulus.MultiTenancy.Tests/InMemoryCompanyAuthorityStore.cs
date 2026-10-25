using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.Tests;

/// <summary>Authority rows in memory: the same rules as the EF store, without a database.</summary>
internal sealed class InMemoryCompanyAuthorityStore : ICompanyAuthorityStore
{
    private readonly List<CompanyAuthorityGrant> _rows = [];

    public Task<bool> HasActiveGrantAsync(Guid parentId, Guid childId, Guid principalId, CompanyOperation operation, CancellationToken ct = default)
        => Task.FromResult(_rows.Any(g => g.IsActive && g.ParentId == parentId && g.ChildId == childId && g.PrincipalId == principalId && g.Operation == operation));

    public Task<CompanyAuthorityGrant> GrantAsync(Guid parentId, Guid childId, Guid principalId, CompanyOperation operation, Guid? grantedBy, DateTimeOffset now, CancellationToken ct = default)
    {
        var grant = new CompanyAuthorityGrant(Guid.NewGuid(), parentId, childId, principalId, operation, grantedBy, now);
        _rows.Add(grant);
        return Task.FromResult(grant);
    }

    public Task<bool> RevokeAsync(Guid id, Guid? revokedBy, DateTimeOffset now, CancellationToken ct = default)
    {
        var index = _rows.FindIndex(g => g.Id == id && g.IsActive);
        if (index < 0)
            return Task.FromResult(false);
        _rows[index] = _rows[index] with { RevokedAt = now, RevokedBy = revokedBy };
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<CompanyAuthorityGrant>> ListAsync(Guid parentId, Guid childId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CompanyAuthorityGrant>>(_rows.Where(g => g.ParentId == parentId && g.ChildId == childId).ToList());

    public Task<CompanyAuthorityGrant?> FindAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_rows.FirstOrDefault(g => g.Id == id));
}
