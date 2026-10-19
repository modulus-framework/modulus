namespace Modulus.Authorization.Dimensions;

/// <summary>
/// A typed value of a scope dimension, such as the warehouse <c>WH-01</c> or the region <c>NORTH</c>. Scope assignments and record
/// keys compare ids, so the business code is kept here and resolved to <see cref="Id"/> when a scope is granted.
/// </summary>
/// <param name="Id">The id that assignments and record access keys carry.</param>
/// <param name="Dimension">The dimension the value belongs to (an assignment type, for example <c>warehouse</c>).</param>
/// <param name="Code">The business code, upper-case and trimmed; unique within its dimension and company.</param>
/// <param name="Label">A display name.</param>
/// <param name="ParentId">The value this one sits under (a warehouse under a region), or null for a top-level value.</param>
public sealed record DimensionValue(Guid Id, string Dimension, string Code, string Label, Guid? ParentId = null);

/// <summary>
/// The codes of each scope dimension, per company. A code is registered once and keeps its id; registering it again returns the same value,
/// so an import or a retry cannot create a second warehouse with the same code.
/// </summary>
public interface IDimensionValueRegistry
{
    /// <summary>Registers <paramref name="code"/> in <paramref name="dimension"/>, or returns the existing value for it.</summary>
    Task<DimensionValue> RegisterAsync(string dimension, string code, string label, CancellationToken ct = default);

    /// <summary>The id of <paramref name="code"/> in <paramref name="dimension"/> in the current company, or null when there is none.</summary>
    Task<Guid?> ResolveIdAsync(string dimension, string code, CancellationToken ct = default);

    /// <summary>The values of <paramref name="dimension"/> in the current company, by code.</summary>
    Task<IReadOnlyList<DimensionValue>> ListAsync(string dimension, CancellationToken ct = default);

    /// <summary>
    /// Places <paramref name="code"/> under the parent value (or at the top when both parent arguments are null). A value cannot be its own
    /// ancestor, so a placement that would close a loop is refused.
    /// </summary>
    /// <exception cref="Modulus.Core.Abstractions.Exceptions.NotFoundException">The value or the parent is not registered in the company.</exception>
    /// <exception cref="ArgumentException">The placement is a loop, or a name is invalid.</exception>
    Task<DimensionValue> SetParentAsync(string dimension, string code, string? parentDimension, string? parentCode, CancellationToken ct = default);
}

/// <summary>
/// Expands assigned targets to the values under them, so an assignment to a region covers the warehouses placed beneath it. Used by the
/// scope subject once per assignment type per request, so it is synchronous like the assignment store.
/// </summary>
public interface IDimensionHierarchy
{
    /// <summary><paramref name="targets"/> together with every value placed (directly or through others) beneath them, in the current company.</summary>
    IReadOnlySet<Guid> Expand(IReadOnlySet<Guid> targets);
}

/// <summary>The default <see cref="IDimensionHierarchy"/>: no placements, so the targets are returned unchanged.</summary>
public sealed class NullDimensionHierarchy : IDimensionHierarchy
{
    /// <inheritdoc />
    public IReadOnlySet<Guid> Expand(IReadOnlySet<Guid> targets) => targets;
}

/// <summary>Normalisation of dimension names and codes, shared by every registry so they agree on what a code is.</summary>
public static class DimensionKeys
{
    private const int MaxLength = 64;

    /// <summary>The dimension name in canonical form: lower-case, starting with a letter, letters digits and hyphens only.</summary>
    /// <exception cref="ArgumentException">The name is not a valid dimension name.</exception>
    public static string Dimension(string dimension)
    {
        var name = dimension?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length == 0 || name.Length > MaxLength || !char.IsAsciiLetterLower(name[0])
            || !name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            throw new ArgumentException("A dimension name is lower-case letters, digits and hyphens, starting with a letter.", nameof(dimension));
        return name;
    }

    /// <summary>The code in canonical form: trimmed and upper-case, so <c>wh-01</c> and <c>WH-01</c> are the same value.</summary>
    /// <exception cref="ArgumentException">The code is empty or longer than 64 characters.</exception>
    public static string Code(string code)
    {
        var value = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (value.Length == 0 || value.Length > MaxLength)
            throw new ArgumentException("A dimension code is 1 to 64 characters.", nameof(code));
        return value;
    }
}
