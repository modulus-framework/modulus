using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.OrgStructure.Domain.ValueObjects;

public readonly record struct OrgNodeId(Guid Value)
{
    public static OrgNodeId New() => new(Guid.NewGuid());
    public static OrgNodeId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
