namespace TradeFlow.Modules.OrgStructure.Domain.ValueObjects;

public readonly record struct PositionId(Guid Value)
{
    public static PositionId New() => new(Guid.NewGuid());
    public static PositionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
