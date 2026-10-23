namespace Modulus.Authorization.Sharing;

/// <summary>Durable home of <see cref="RecordShare"/>s. Every query is scoped by the company, so a store never returns another company's shares.</summary>
public interface IRecordShareStore
{
    Task AddAsync(RecordShare share, CancellationToken ct = default);

    Task<RecordShare?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>The shares of one record in one company, for the grantee check.</summary>
    Task<IReadOnlyList<RecordShare>> ForRecordAsync(Guid? tenantId, string resourceType, string resourceId, CancellationToken ct = default);

    Task UpdateAsync(RecordShare share, CancellationToken ct = default);
}
