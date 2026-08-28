using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.OrgStructure.Domain.Errors;

public static class OrgStructureErrors
{
    public static Error NotFound(Guid nodeId) =>
        Error.NotFound("OrgNode.NotFound", $"Organization node '{nodeId}' was not found");

    public static readonly Error DuplicateCode = Error.Conflict(
        "OrgNode.DuplicateCode",
        "An organization node with the same code already exists under this parent");

    public static Error PositionNotFound(Guid positionId) =>
        Error.NotFound("Position.NotFound", $"Position '{positionId}' was not found");

    public static readonly Error DuplicatePositionCode = Error.Conflict(
        "Position.DuplicateCode",
        "A position with the same code already exists in this organization node");

    public static Error CircularReference(Guid nodeId) =>
        Error.Validation("OrgNode.CircularReference",
            $"Adding node '{nodeId}' as a child would create a circular reference");

    public static Error NodeHasChildren(Guid nodeId) =>
        Error.BusinessRule("OrgNode.HasChildren",
            "Cannot deactivate a node that has active children. Deactivate children first.");
}
