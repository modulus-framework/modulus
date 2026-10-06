using System.Text;

namespace Modulus.Cli.Services;

/// <summary>
/// Mirrors <c>IntegrationEventNaming.Derive</c> in the framework (the CLI cannot reference it), so the events a
/// generated host exposes carry the same names the runtime computes from <c>[IntegrationEvent&lt;TModule&gt;]</c>.
/// A test pins the two together.
/// </summary>
internal static class IntegrationEventNames
{
    private static readonly string[] s_moduleSuffixes = ["Module", "Area", "Marker"];
    private static readonly string[] s_eventSuffixes = ["IntegrationEvent", "Event"];

    public static string Derive(string moduleTypeName, string eventTypeName, int version = 1)
        => $"{Kebab(Strip(moduleTypeName, s_moduleSuffixes))}.{Kebab(Strip(eventTypeName, s_eventSuffixes))}.v{version}";

    internal static string Kebab(string pascal)
    {
        var builder = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (!char.IsLetterOrDigit(c))
                continue;

            if (char.IsUpper(c) && builder.Length > 0)
            {
                var previousLower = char.IsLower(pascal[i - 1]) || char.IsDigit(pascal[i - 1]);
                var acronymEnds = char.IsUpper(pascal[i - 1]) && i + 1 < pascal.Length && char.IsLower(pascal[i + 1]);
                if (previousLower || acronymEnds)
                    builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string Strip(string name, string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length];
        }

        return name;
    }
}
