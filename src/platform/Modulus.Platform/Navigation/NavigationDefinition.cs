namespace Modulus.Navigation;

using System.Text.RegularExpressions;

/// <summary>
/// The menu as the contributors declared it, checked once: unique keys, parents that exist, no cycles, a bounded depth, paths that stay
/// inside the application, and no empty title. A menu that fails the check does not start (see <see cref="NavigationStartupCheck"/>).
/// </summary>
public sealed partial class NavigationDefinition
{
    /// <summary>The deepest a menu may nest.</summary>
    public const int MaxDepth = 8;

    /// <summary>The longest title.</summary>
    public const int MaxTitleLength = 128;

    private static readonly Regex KeyPattern = KeyRegex();

    private NavigationDefinition(IReadOnlyList<NavigationItem> items)
    {
        Items = items;
        ByKey = items.ToDictionary(i => i.Key, StringComparer.Ordinal);
    }

    /// <summary>Every declared item, in declaration order.</summary>
    public IReadOnlyList<NavigationItem> Items { get; }

    /// <summary>The items by key.</summary>
    public IReadOnlyDictionary<string, NavigationItem> ByKey { get; }

    /// <summary>Checks the items and builds the definition. Throws <see cref="InvalidOperationException"/> listing every problem.</summary>
    public static NavigationDefinition Build(IEnumerable<NavigationItem> items)
    {
        var list = items.ToList();
        var problems = Validate(list);
        if (problems.Count > 0)
            throw new InvalidOperationException("The navigation menu is invalid: " + string.Join(" ", problems));
        return new NavigationDefinition(list);
    }

    /// <summary>Returns every problem with <paramref name="items"/>, empty when the menu is valid.</summary>
    internal static List<string> Validate(IReadOnlyList<NavigationItem> items)
    {
        var problems = new List<string>();
        var byKey = new Dictionary<string, NavigationItem>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item.Key is null || !KeyPattern.IsMatch(item.Key))
                problems.Add($"'{item.Key}' is not a valid key.");
            else if (!byKey.TryAdd(item.Key, item))
                problems.Add($"'{item.Key}' is declared more than once.");

            if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > MaxTitleLength)
                problems.Add($"'{item.Key}' needs a title of at most {MaxTitleLength} characters.");

            if (item.Url is not null && !IsLocalPath(item.Url))
                problems.Add($"'{item.Key}' links outside the application: '{item.Url}'.");

            if (item.Permission is not null && string.IsNullOrWhiteSpace(item.Permission))
                problems.Add($"'{item.Key}' has an empty permission.");
        }

        foreach (var item in byKey.Values)
        {
            if (item.ParentKey is null)
                continue;
            if (!byKey.ContainsKey(item.ParentKey))
                problems.Add($"'{item.Key}' names a parent that is not declared: '{item.ParentKey}'.");
        }

        foreach (var item in byKey.Values)
        {
            var depth = Depth(item.Key, byKey, out var cyclic);
            if (cyclic)
                problems.Add($"'{item.Key}' is its own ancestor.");
            else if (depth > MaxDepth)
                problems.Add($"'{item.Key}' nests deeper than {MaxDepth} levels.");
        }

        return problems;
    }

    /// <summary>Whether <paramref name="url"/> is a path inside the application (not a scheme, a network path or a backslash trick).</summary>
    internal static bool IsLocalPath(string url)
        => url.Length > 0 && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\', StringComparison.Ordinal);

    /// <summary>The number of ancestors of <paramref name="key"/>, counting itself; <paramref name="cyclic"/> when the chain loops.</summary>
    internal static int Depth(string key, IReadOnlyDictionary<string, NavigationItem> byKey, out bool cyclic)
        => Depth(key, id => byKey.TryGetValue(id, out var item) ? item.ParentKey : null, out cyclic);

    /// <summary>The same walk over any parent lookup, so an override can be checked before it is stored.</summary>
    internal static int Depth(string key, Func<string, string?> parentOf, out bool cyclic)
    {
        cyclic = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var depth = 0;
        for (string? current = key; current is not null; current = parentOf(current))
        {
            if (!seen.Add(current))
            {
                cyclic = true;
                return int.MaxValue;
            }

            depth++;
            if (depth > MaxDepth + 1)
                return depth;
        }

        return depth;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();
}
