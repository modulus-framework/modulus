namespace ProcureFlow.Modules.OrgStructure.Domain.Entities;

public sealed class PositionAssignment
{
    public Guid Id { get; private set; }
    public Guid PositionId { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? DelegationNote { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string? DeactivatedBy { get; private set; }
    public DateTime? DeactivatedAtUtc { get; private set; }

    private PositionAssignment() { }

    private PositionAssignment(Guid positionId, Guid userId, DateOnly effectiveFrom,
        DateOnly? effectiveTo, string createdBy)
    {
        Id = Guid.NewGuid();
        PositionId = positionId;
        UserId = userId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public static PositionAssignment Create(Guid positionId, Guid userId,
        DateOnly effectiveFrom, DateOnly? effectiveTo, string createdBy)
        => new(positionId, userId, effectiveFrom, effectiveTo, createdBy);

    public bool EffectiveOn(DateOnly date)
        => date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    public void Deactivate(string performedBy)
    {
        IsActive = false;
        EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow);
        DeactivatedBy = performedBy;
        DeactivatedAtUtc = DateTime.UtcNow;
    }
}
