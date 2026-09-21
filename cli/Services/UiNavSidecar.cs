using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// Keeps the app-owned nav sidecar (<c>Ui/{Module}UiModule.cs</c>) in step with the CRUD pages generated into the module.
/// The file is written once, so a second entity in the same module would otherwise never reach the sidebar, and a page
/// generated before permissions existed would keep an unguarded menu entry. Pure string transforms over the shape the
/// template writes; a hand-edited file that no longer has that shape is left alone.
/// </summary>
internal static partial class UiNavSidecar
{
    /// <summary>
    /// Returns <paramref name="content"/> with a sidebar item for the entity's page: added when the module has none for it yet
    /// (and the entity listed in the manifest's features), else, when the existing one has no <c>requiredPermission</c> and a
    /// <paramref name="permission"/> applies, given it. Idempotent.
    /// </summary>
    public static string EnsureItem(string content, string moduleName, string entityPlural, string route, string? permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityPlural);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        var nl = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var moduleLower = moduleName.ToLowerInvariant();
        var itemId = $"{moduleName}.{entityPlural}";

        if (content.Contains($"\"{itemId}\"", StringComparison.Ordinal))
            return permission is null ? content : GuardExistingItem(content, itemId, permission, nl);

        // The item goes in front of the parenthesis that closes base(...): the nav lambda is its last argument.
        var end = NavEnd().Match(content);
        if (!end.Success)
            return content;

        var item =
            $"{nl}                .AddItem(" +
            $"{nl}                    \"{itemId}\"," +
            $"{nl}                    \"{entityPlural}\"," +
            $"{nl}                    \"/{moduleLower}/{route}\"," +
            $"{nl}                    groupId: \"{moduleLower}\"" +
            (permission is null ? string.Empty : $",{nl}                    requiredPermission: \"{permission}\"") +
            ")";
        content = content.Insert(end.Index + 1, item);
        return AddFeature(content, entityPlural);
    }

    private static string GuardExistingItem(string content, string itemId, string permission, string nl)
    {
        var item = Regex.Match(
            content,
            $@"(""{Regex.Escape(itemId)}"",\s*""[^""]*"",\s*""[^""]*"",\s*groupId:\s*""[^""]*"")(\s*\))",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));
        if (!item.Success)
            return content;

        return content.Insert(item.Groups[1].Index + item.Groups[1].Length, $",{nl}                    requiredPermission: \"{permission}\"");
    }

    private static string AddFeature(string content, string entityPlural)
    {
        // The features array is the last argument of the manifest, i.e. the last bracket before the nav lambda.
        var nav = content.IndexOf("nav =>", StringComparison.Ordinal);
        var close = nav < 0 ? -1 : content.LastIndexOf(']', nav);
        var open = close < 0 ? -1 : content.LastIndexOf('[', close);
        if (open < 0)
            return content;

        var features = content[(open + 1)..close];
        if (features.Contains($"\"{entityPlural}\"", StringComparison.Ordinal))
            return content;

        return content.Insert(close, (features.Trim().Length == 0 ? string.Empty : ", ") + $"\"{entityPlural}\"");
    }

    [GeneratedRegex(@"\)\)(?=\s*\{)", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex NavEnd();
}
