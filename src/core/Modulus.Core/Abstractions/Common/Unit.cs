namespace Modulus.Core.Abstractions.Common;

/// <summary>
/// Represents the absence of a meaningful return value.
/// Use as TResponse for commands with no result: ICommand{Unit}.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = default;
    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "()";
    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
}