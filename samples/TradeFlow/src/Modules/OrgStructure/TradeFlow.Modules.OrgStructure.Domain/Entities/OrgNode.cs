using TradeFlow.Modules.OrgStructure.Domain.Enums;
using TradeFlow.Modules.OrgStructure.Domain.Events;
using TradeFlow.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace TradeFlow.Modules.OrgStructure.Domain.Entities;

public sealed class OrgNode : AggregateRoot, IAuditableEntity
{
    private readonly List<OrgNode> _children = [];

    public new Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? ParentId { get; private set; }
    public OrgNodeType NodeType { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? NameBn { get; private set; }
    public string LtreePath { get; private set; } = string.Empty;
    public int Depth { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? CustomsAttributesJson { get; private set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public IReadOnlyList<OrgNode> Children => _children;

    private OrgNode() { }

    private OrgNode(
        Guid id, Guid tenantId, Guid? parentId, OrgNodeType nodeType,
        string code, string name, string? nameBn, string ltreePath,
        int depth, DateOnly effectiveFrom, DateOnly? effectiveTo,
        string? customsAttributesJson, string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        ParentId = parentId;
        NodeType = nodeType;
        Code = code;
        Name = name;
        NameBn = nameBn;
        LtreePath = ltreePath;
        Depth = depth;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        CustomsAttributesJson = customsAttributesJson;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
    }

    public static Result<OrgNode> Create(
        Guid id, Guid tenantId, Guid? parentId, OrgNodeType nodeType,
        string code, string name, string? nameBn,
        DateOnly effectiveFrom, DateOnly? effectiveTo,
        string? customsAttributesJson, string createdBy,
        string parentLtreePath = "", int parentDepth = -1)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<OrgNode>(Error.Validation("OrgNode.EmptyCode", "Node code is required"));
        if (code.Length > 50)
            return Result.Failure<OrgNode>(Error.Validation("OrgNode.CodeTooLong", "Node code cannot exceed 50 characters"));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<OrgNode>(Error.Validation("OrgNode.EmptyName", "Node name is required"));
        if (name.Length > 200)
            return Result.Failure<OrgNode>(Error.Validation("OrgNode.NameTooLong", "Node name cannot exceed 200 characters"));
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
            return Result.Failure<OrgNode>(Error.Validation("OrgNode.BadEffectiveWindow", "EffectiveTo must be after EffectiveFrom"));

        string ltreePath = string.IsNullOrEmpty(parentLtreePath)
            ? code.ToLowerInvariant()
            : $"{parentLtreePath}.{code.ToLowerInvariant()}";
        int depth = parentDepth + 1;

        return Result.Success(new OrgNode(
            id, tenantId, parentId, nodeType, code, name, nameBn,
            ltreePath, depth, effectiveFrom, effectiveTo,
            customsAttributesJson, createdBy));
    }

    public Result Update(string name, string? nameBn, DateOnly? effectiveTo, string? customsAttributesJson, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("OrgNode.EmptyName", "Node name is required"));
        Name = name;
        NameBn = nameBn;
        EffectiveTo = effectiveTo;
        CustomsAttributesJson = customsAttributesJson;
        UpdatedBy = updatedBy;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Deactivate(string performedBy)
    {
        if (!IsActive)
            return Result.Failure(Error.BusinessRule("OrgNode.AlreadyInactive", "Node is already inactive"));
        IsActive = false;
        EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow);
        UpdatedBy = performedBy;
        UpdatedAt = DateTime.UtcNow;
        Raise(new OrgNodeDeactivatedDomainEvent(Guid.NewGuid(), Id, TenantId, NodeType.ToString(), DateTime.UtcNow));
        return Result.Success();
    }

    public bool IsEffectiveOn(DateOnly date)
        => date >= EffectiveFrom && (!EffectiveTo.HasValue || date <= EffectiveTo.Value);

    public bool IsAncestorOf(string otherLtreePath)
        => otherLtreePath.StartsWith(LtreePath + ".") || otherLtreePath == LtreePath;
}
