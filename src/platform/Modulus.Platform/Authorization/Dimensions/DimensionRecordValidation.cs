namespace Modulus.Authorization.Dimensions;

using System.Reflection;
using Modulus.Core.Abstractions.Dimensions;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// The create and update check for a record's dimension value: a record may name only a code that is registered for its
/// company, so it cannot carry a warehouse or region that scope assignments and access keys do not know.
/// </summary>
public static class DimensionRecordValidation
{
    /// <summary>
    /// Checks every property marked <see cref="DimensionAttribute"/> on <paramref name="record"/>: a set id must be registered for the
    /// current company in the property's dimension. A null value is not checked. Generated create and update handlers call this before
    /// they save.
    /// </summary>
    /// <exception cref="ValidationException">A set id is not registered for the company in its dimension; the error names the property.</exception>
    /// <exception cref="InvalidOperationException">A property marked with the attribute is not a <see cref="Guid"/>.</exception>
    public static async Task EnsureRecordDimensionsAsync<TRecord>(
        this IDimensionValueRegistry registry, TRecord record, CancellationToken ct = default)
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(record);

        foreach (var property in typeof(TRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<DimensionAttribute>() is not { } declared)
                continue;
            if (property.PropertyType != typeof(Guid) && property.PropertyType != typeof(Guid?))
                throw new InvalidOperationException($"{typeof(TRecord).Name}.{property.Name} is marked [Dimension] but is not a Guid.");
            if (property.GetValue(record) is not Guid id)
                continue;

            var dimension = DimensionKeys.Dimension(declared.Dimension);
            var values = await registry.ListAsync(dimension, ct).ConfigureAwait(false);
            if (!values.Any(v => v.Id == id))
                throw new ValidationException([$"{property.Name}: the value is not a registered {dimension} for this company."]);
        }
    }

    /// <summary>
    /// Resolves the <paramref name="code"/> a record names in <paramref name="dimension"/> to the id it carries, for the current
    /// company. An unregistered or malformed code refuses the write with a <see cref="ValidationException"/> naming the field.
    /// </summary>
    /// <param name="registry">The company's dimension registry.</param>
    /// <param name="dimension">The dimension, such as <c>warehouse</c>.</param>
    /// <param name="field">The record field the code came from, used in the error.</param>
    /// <param name="code">The code the record names.</param>
    /// <param name="ct">Cancels the lookup.</param>
    /// <exception cref="ValidationException">The code is malformed or not registered for the company.</exception>
    public static async Task<Guid> RequireRegisteredAsync(
        this IDimensionValueRegistry registry, string dimension, string field, string code, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        string canonical;
        try
        {
            canonical = DimensionKeys.Code(code);
        }
        catch (ArgumentException)
        {
            throw new ValidationException([$"{field}: a {DimensionKeys.Dimension(dimension)} code is 1 to 64 characters."]);
        }

        var id = await registry.ResolveIdAsync(dimension, canonical, ct).ConfigureAwait(false);
        if (id is null)
            throw new ValidationException([$"{field}: '{canonical}' is not a registered {DimensionKeys.Dimension(dimension)} code."]);

        return id.Value;
    }
}
