namespace TradeFlow.Shared.Domain;

public interface IAuditable
{
    string? CreatedBy { get; }
    string? LastModifiedBy { get; }
    DateTimeOffset? LastModifiedAtUtc { get; }
    void SetCreatedBy(string userId);
    void SetLastModifiedBy(string userId);
}
