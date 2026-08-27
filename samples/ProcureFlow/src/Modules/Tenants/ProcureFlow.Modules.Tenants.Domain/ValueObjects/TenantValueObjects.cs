using System.Text.RegularExpressions;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Tenants.Domain.ValueObjects;

public sealed record TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());
    public static TenantId From(Guid value) => new(value);
    public static TenantId FromString(string value) => new(Guid.Parse(value));
}

public sealed record Subdomain
{
    private static readonly Regex Regex = new(@"^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$", RegexOptions.Compiled);

    public string Value { get; }

    private Subdomain(string value)
    {
        Value = value.ToLowerInvariant().Trim();
    }

    public static Result<Subdomain> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<Subdomain>(Error.Validation("Subdomain.Empty", "Subdomain cannot be empty"));
        }

        string normalized = value.ToLowerInvariant().Trim();

        if (normalized.Length < 3 || normalized.Length > 63)
        {
            return Result.Failure<Subdomain>(Error.Validation("Subdomain.InvalidLength", "Subdomain must be between 3 and 63 characters"));
        }

        if (!Regex.IsMatch(normalized))
        {
            return Result.Failure<Subdomain>(Error.Validation("Subdomain.InvalidFormat", "Subdomain must start and end with alphanumeric characters, and contain only lowercase letters, numbers, and hyphens"));
        }

        return Result.Success(new Subdomain(normalized));
    }

    public static Subdomain FromString(string value) => new(value);
}
