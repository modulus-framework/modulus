namespace TradeFlow.Modules.Identity.Domain.ValueObjects;

public sealed record UserName
{
    public string Value { get; }

    private UserName(string value)
    {
        Value = value;
    }

    internal static UserName Reconstitute(string value) => new(value);

    public static UserName Create(string value)
    {

        return new UserName(value);
    }

    public override string ToString() => Value;
}
