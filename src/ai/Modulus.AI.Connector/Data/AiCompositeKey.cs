namespace Modulus.AI.Connector.Data;

using System.Globalization;
using System.Text;

/// <summary>
/// How the id of a record with a composite key (several key columns) is written on the wire and in the change journal:
/// the parts, each in its invariant form (<see cref="Guid"/> as <c>D</c>), joined with <c>|</c>, with <c>\</c> and <c>|</c> inside a part
/// escaped by a backslash. A resource lookup of such an entity takes a <see cref="string"/> id and reads it back with <see cref="Split"/>.
/// </summary>
public static class AiCompositeKey
{
    /// <summary>The id of a record whose key columns have <paramref name="parts"/> (in key order).</summary>
    public static string Format(params object?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0)
            throw new ArgumentException("A key needs at least one part.", nameof(parts));

        return string.Join('|', parts.Select(part => Escape(part switch
        {
            Guid guid => guid.ToString("D"),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => part?.ToString() ?? string.Empty,
        })));
    }

    /// <summary>The parts of a composite id, unescaped, in key order.</summary>
    /// <exception cref="FormatException">The id ends in a lone escape character.</exception>
    public static IReadOnlyList<string> Split(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var parts = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (c == '\\')
            {
                if (++i >= id.Length)
                    throw new FormatException("A key ends in a lone escape character.");
                current.Append(id[i]);
            }
            else if (c == '|')
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static string Escape(string part) => part.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);
}
