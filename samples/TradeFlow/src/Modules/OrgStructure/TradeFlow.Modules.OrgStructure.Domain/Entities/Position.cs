using TradeFlow.Modules.OrgStructure.Domain.Events;
using TradeFlow.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace TradeFlow.Modules.OrgStructure.Domain.Entities;

public sealed class Position : AggregateRoot, IAuditableEntity
{
    private readonly List<PositionAssignment> _assignments = [];

    public new Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrgNodeId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? TitleBn { get; private set; }
    public bool IsDelegatable { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public IReadOnlyList<PositionAssignment> Assignments => _assignments;

    private Position() { }

    private Position(Guid id, Guid tenantId, Guid orgNodeId, string code,
        string title, string? titleBn, bool isDelegatable, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        OrgNodeId = orgNodeId;
        Code = code;
        Title = title;
        TitleBn = titleBn;
        IsDelegatable = isDelegatable;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
    }

    public static Result<Position> Create(
        Guid id, Guid tenantId, Guid orgNodeId, string code,
        string title, string? titleBn, bool isDelegatable, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<Position>(Error.Validation("Position.EmptyCode", "Position code is required"));
        if (code.Length > 50)
            return Result.Failure<Position>(Error.Validation("Position.CodeTooLong", "Position code cannot exceed 50 characters"));
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure<Position>(Error.Validation("Position.EmptyTitle", "Position title is required"));
        if (title.Length > 200)
            return Result.Failure<Position>(Error.Validation("Position.TitleTooLong", "Position title cannot exceed 200 characters"));

        return Result.Success(new Position(id, tenantId, orgNodeId, code, title, titleBn, isDelegatable, createdBy));
    }

    public Result Assign(Guid userId, DateOnly effectiveFrom, DateOnly? effectiveTo, string assignedBy)
    {
        if (!IsActive)
            return Result.Failure(Error.BusinessRule("Position.Inactive", "Cannot assign to an inactive position"));
        if (_assignments.Any(a => a.UserId == userId && a.IsActive))
            return Result.Failure(Error.Conflict("Position.AlreadyAssigned", "User is already assigned to this position"));
        _assignments.Add(PositionAssignment.Create(Id, userId, effectiveFrom, effectiveTo, assignedBy));
        Raise(new PositionAssignedDomainEvent(Guid.NewGuid(), Id, userId, TenantId, DateTime.UtcNow));
        return Result.Success();
    }

    public Result Unassign(Guid assignmentId, string performedBy)
    {
        PositionAssignment? assignment = _assignments.FirstOrDefault(a => a.Id == assignmentId);
        if (assignment is null)
            return Result.Failure(Error.NotFound("Position.AssignmentNotFound", "Assignment not found"));
        assignment.Deactivate(performedBy);
        return Result.Success();
    }

    public bool HasActiveAssignee(DateOnly onDate)
        => _assignments.Any(a => a.IsActive && a.EffectiveOn(onDate));

    public IReadOnlyList<Guid> GetActiveUserIds(DateOnly onDate)
        => _assignments.Where(a => a.IsActive && a.EffectiveOn(onDate)).Select(a => a.UserId).ToList();
}
