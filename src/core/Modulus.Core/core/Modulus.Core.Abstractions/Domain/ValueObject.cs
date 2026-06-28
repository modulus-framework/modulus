namespace Modulus.Core.Abstractions.Domain;

/// <summary>
/// Base class for DDD value objects with structural equality.
/// Override <see cref="GetEqualityComponents"/> to specify the
/// atomic values that participate in equality.
/// </summary>
public abstract class ValueObject
{
    /// <summary>
    /// Yields the atomic components that define equality.
    /// Order matters — components are compared positionally.
    /// </summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
            return false;

        var other = (ValueObject)obj;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
            hash.Add(component);
        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left?.Equals(right) ?? right is null;

    public static bool operator !=(ValueObject? left, ValueObject? right)
        => !(left == right);
}

/// <summary>
/// Convenience base that extracts equality components from
/// the public get-only properties declared on the derived type.
/// </summary>
public abstract class AutoValueObject : ValueObject
{
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var prop in GetType().GetProperties())
        {
            if (prop.GetIndexParameters().Length == 0)
                yield return prop.GetValue(this);
        }
    }
}
