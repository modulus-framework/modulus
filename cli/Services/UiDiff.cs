using System.Text;

namespace Modulus.Cli.Services;

/// <summary>How an app's copy of a component view relates to the framework's current one.</summary>
internal enum UiViewStatus
{
    /// <summary>Same as the framework's current view: nothing customized (the copy could be deleted).</summary>
    Identical,

    /// <summary>Changed by the app; the framework's view is the same as when it was ejected.</summary>
    Customized,

    /// <summary>Not changed by the app, but the framework's view changed since: re-eject with <c>--force</c> to take the update.</summary>
    Outdated,

    /// <summary>Changed by the app <b>and</b> by the framework since the eject: merge by hand.</summary>
    Conflict,

    /// <summary>No eject marker (an override written by hand), so there is no way to know whether the framework changed.</summary>
    Unmarked,
}

/// <summary>One ejected (or hand-written) override compared with the framework's current view.</summary>
/// <param name="Framework">The framework's current view.</param>
/// <param name="Path">The app's file.</param>
/// <param name="Status">How the app's copy relates to the framework's current view.</param>
/// <param name="EjectedFrom">Framework version recorded in the eject marker; null for a hand-written override.</param>
/// <param name="Diff">Unified-style lines (framework to app copy); empty when the two are equal.</param>
internal sealed record UiViewDiff(UiView Framework, string Path, UiViewStatus Status, string? EjectedFrom, IReadOnlyList<string> Diff);

/// <summary>Compares the app's overrides of framework views with the framework's current views (<c>modulus ui diff</c>).</summary>
internal static class UiDiff
{
    /// <summary>
    /// Every framework view the app overrides, whatever its kind (component view, feature UI page or partial, theme layout), compared
    /// with the framework's current one. A view is overridden when the app has a file at its own path (<see cref="UiView.AppPath"/>);
    /// files the framework has no view for (the app's own pages and components) are never looked at. A <c>_ViewImports</c> counts only when
    /// <c>modulus ui eject</c> wrote it (it has the marker): an app's own imports file at the same path is not an override of anything.
    /// <paramref name="target"/> narrows the result to what a name stands for (see <see cref="UiViewCatalog.Match"/>): a component,
    /// a feature UI or theme, or one view.
    /// </summary>
    public static IReadOnlyList<UiViewDiff> Compare(string apiDir, string? target = null)
    {
        IEnumerable<UiView> views = string.IsNullOrWhiteSpace(target) ? UiViewCatalog.All : UiViewCatalog.Match(target);

        var results = new List<UiViewDiff>();
        foreach (var view in views)
        {
            var file = UiEject.PathFor(apiDir, view);
            if (!File.Exists(file))
            {
                continue;
            }

            var diff = Classify(view, file);
            if (view.IsViewImports && diff.Status == UiViewStatus.Unmarked)
            {
                continue;
            }

            results.Add(diff);
        }

        return results;
    }

    /// <summary>Classifies one app file against the framework's current <paramref name="framework"/> view.</summary>
    public static UiViewDiff Classify(UiView framework, string file)
    {
        _ = UiViewMarker.TryParse(File.ReadAllText(file), out var info, out var body);
        var bodyHash = UiViewMarker.Hash(body);
        var same = bodyHash == framework.Hash;

        UiViewStatus status;
        if (same)
        {
            status = UiViewStatus.Identical;
        }
        else if (info is not null)
        {
            status = Relate(info.BaseHash, bodyHash, framework.Hash);
        }
        else
        {
            status = UiViewStatus.Unmarked;
        }

        return new UiViewDiff(framework, file, status, info?.FrameworkVersion, same ? [] : Lines(framework.Source, body));
    }

    // base = what the framework had at eject time, local = the app's copy now, current = what the framework has now.
    private static UiViewStatus Relate(string baseHash, string localHash, string currentHash)
    {
        var appChanged = localHash != baseHash;
        var frameworkChanged = currentHash != baseHash;
        return (appChanged, frameworkChanged) switch
        {
            (true, true) => UiViewStatus.Conflict,
            (true, false) => UiViewStatus.Customized,
            _ => UiViewStatus.Outdated,
        };
    }

    /// <summary>
    /// A small line diff: <c>-</c> lines only in <paramref name="framework"/>, <c>+</c> lines only in
    /// <paramref name="app"/>, unchanged lines kept as context (two either side of a change, <c>...</c> between hunks).
    /// Views are short, so a plain longest-common-subsequence table is plenty.
    /// </summary>
    public static IReadOnlyList<string> Lines(string framework, string app)
    {
        var a = framework.TrimEnd('\n').Split('\n');
        var b = app.TrimEnd('\n').Split('\n');

        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var edits = new List<(char Kind, string Text)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                edits.Add((' ', a[x])); x++; y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                edits.Add(('-', a[x])); x++;
            }
            else
            {
                edits.Add(('+', b[y])); y++;
            }
        }

        while (x < a.Length) { edits.Add(('-', a[x++])); }
        while (y < b.Length) { edits.Add(('+', b[y++])); }

        return WithContext(edits, context: 2);
    }

    private static List<string> WithContext(List<(char Kind, string Text)> edits, int context)
    {
        var show = new bool[edits.Count];
        for (var i = 0; i < edits.Count; i++)
        {
            if (edits[i].Kind == ' ')
            {
                continue;
            }

            for (var k = Math.Max(0, i - context); k <= Math.Min(edits.Count - 1, i + context); k++)
            {
                show[k] = true;
            }
        }

        var lines = new List<string>();
        var skipped = false;
        for (var i = 0; i < edits.Count; i++)
        {
            if (!show[i])
            {
                skipped = lines.Count > 0;
                continue;
            }

            if (skipped)
            {
                lines.Add("...");
                skipped = false;
            }

            lines.Add($"{edits[i].Kind}{edits[i].Text}");
        }

        return lines;
    }

    /// <summary>The diff as one string (for tests and plain output).</summary>
    public static string Render(IReadOnlyList<string> diff)
        => new StringBuilder().AppendJoin('\n', diff).ToString();
}
