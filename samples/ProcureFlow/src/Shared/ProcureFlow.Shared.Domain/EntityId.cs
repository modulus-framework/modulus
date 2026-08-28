namespace ProcureFlow.Shared.Domain;

public abstract record EntityId<T>(T Value) : IComparable<EntityId<T>>
    where T : IComparable<T>
{
    public virtual bool Equals(EntityId<T>? other)
    {
        return other is not null && Value.Equals(other.Value);
    }

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(EntityId<T>? other)
    {
        return other is null ? 1 : Value.CompareTo(other.Value);
    }

    public override string ToString() => Value.ToString()!;

    public static bool operator <(EntityId<T>? left, EntityId<T>? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(EntityId<T>? left, EntityId<T> right) => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(EntityId<T>? left, EntityId<T> right) =>
        left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(EntityId<T>? left, EntityId<T>? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;
}
