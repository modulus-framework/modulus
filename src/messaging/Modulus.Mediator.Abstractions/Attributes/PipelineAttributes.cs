namespace Modulus.Mediator.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class SkipValidationAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class SkipTransactionAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class CacheForAttribute(int seconds) : Attribute
{
    public int Seconds { get; } = seconds;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequirePermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}