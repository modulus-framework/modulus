using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>One accessibility rule the markup scan checks, with the WCAG success criterion and level it belongs to.</summary>
internal sealed record AuditRule(string Id, string Criterion, string Level, string Title, string Advice);

/// <summary>A place in a file where a rule is not met.</summary>
internal sealed record AuditIssue(string Rule, string File, int Line, string Snippet);

/// <summary>
/// A static scan of Razor markup (<c>.cshtml</c>, <c>.razor</c>, <c>.html</c>) for accessibility problems that can be found in the
/// source alone. It is a lint, not a WCAG audit: colour contrast, focus order in the running page and content quality need a browser.
/// </summary>
internal static partial class UiAudit
{
    public static readonly IReadOnlyList<AuditRule> Rules =
    [
        new("A11Y001", "1.1.1", "A", "Image without alt text", "Add alt=\"…\" (alt=\"\" for a decorative image)."),
        new("A11Y002", "1.3.1", "A", "Form control without a label", "Add a <label for> (or asp-for), or aria-label / aria-labelledby."),
        new("A11Y003", "4.1.2", "A", "Button or link without an accessible name", "Give it text, or aria-label / title."),
        new("A11Y004", "2.1.1", "A", "Click handler on an element a keyboard cannot reach", "Use a <button> or <a>, or add role and tabindex=\"0\" with a key handler."),
        new("A11Y005", "3.1.1", "A", "Page without a language", "Add lang=\"…\" to <html>."),
        new("A11Y006", "1.3.1", "A", "Table without header cells", "Add <th> cells (scope=\"col\" / \"row\")."),
        new("A11Y007", "2.4.3", "A", "Positive tabindex", "Use tabindex=\"0\" or \"-1\" and let the document order decide."),
    ];

    private static readonly string[] Extensions = [".cshtml", ".razor", ".html"];
    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", "wwwroot", ".git", "Themes"];

    [GeneratedRegex(@"@\*.*?\*@|<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"<(?<tag>img|input|select|textarea|button|a|div|span|li|td|tr|table|html|iframe)\b(?<attrs>(?:[^<>""']|""[^""]*""|'[^']*')*?)(?<self>/?)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Tags();

    [GeneratedRegex(@"(?<name>[A-Za-z_:@][\w:.@-]*)\s*(?:=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+)))?", RegexOptions.Singleline)]
    private static partial Regex Attributes();

    [GeneratedRegex(@"<label\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Labels();

    [GeneratedRegex(@"<th[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderCell();

    public static IReadOnlyList<string> FindFiles(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar)
                .Any(part => SkippedFolders.Contains(part, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.Ordinal)];

    public static IReadOnlyList<AuditIssue> ScanFiles(string root, IEnumerable<string> files)
    {
        var issues = new List<AuditIssue>();
        foreach (var file in files)
            issues.AddRange(Scan(File.ReadAllText(file), Path.GetRelativePath(root, file).Replace('\\', '/')));
        return issues;
    }

    public static IReadOnlyList<AuditIssue> Scan(string markup, string file)
    {
        // Comments keep their length (blanked) so line numbers stay right.
        var text = Comments().Replace(markup, m => Regex.Replace(m.Value, @"[^\r\n]", " "));
        var issues = new List<AuditIssue>();
        var labelledFor = LabelTargets(text);
        var labelSpans = LabelSpans(text);

        void Add(string rule, Match m) => issues.Add(new AuditIssue(rule, file, LineOf(text, m.Index), Snippet(markup, m)));

        var hasHeaderCell = HeaderCell().IsMatch(text);
        foreach (Match m in Tags().Matches(text))
        {
            var tag = m.Groups["tag"].Value.ToLowerInvariant();
            var attrs = ParseAttributes(m.Groups["attrs"].Value);
            bool Has(string name) => attrs.ContainsKey(name);

            switch (tag)
            {
                case "img" when !Has("alt") && !Has("role"):
                    Add("A11Y001", m);
                    break;

                case "input" or "select" or "textarea":
                    if (tag == "input" && attrs.GetValueOrDefault("type") is "hidden" or "submit" or "button" or "reset" or "image")
                        break;
                    // An input inside <label>…</label> is labelled by it.
                    if (IsNamed(attrs) || IsLabelled(attrs, labelledFor) || labelSpans.Any(span => m.Index > span.Start && m.Index < span.End)) break;
                    Add("A11Y002", m);
                    break;

                case "button" or "a":
                    if (tag == "a" && !Has("href") && !Has("@onclick") && !Has("asp-page") && !Has("asp-action")) break;
                    if (IsNamed(attrs)) break;
                    if (m.Groups["self"].Length == 0 && !HasContent(text, m, tag)) Add("A11Y003", m);
                    break;

                case "div" or "span" or "li" or "td" or "tr":
                    if ((Has("onclick") || Has("@onclick")) && !Has("role") && !Has("tabindex")) Add("A11Y004", m);
                    break;

                case "html" when !Has("lang"):
                    Add("A11Y005", m);
                    break;

                case "table" when !hasHeaderCell && !Has("role"):
                    Add("A11Y006", m);
                    break;
            }

            if (attrs.TryGetValue("tabindex", out var tab) && int.TryParse(tab, out var n) && n > 0)
                Add("A11Y007", m);
        }
        return issues;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static Dictionary<string, string> ParseAttributes(string attrs)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attributes().Matches(attrs))
            result[m.Groups["name"].Value] = m.Groups["v"].Success ? m.Groups["v"].Value : "";
        return result;
    }

    /// <summary>Has a name of its own: aria-label, aria-labelledby, title, or (inputs) a button's value.</summary>
    private static bool IsNamed(Dictionary<string, string> attrs) =>
        attrs.ContainsKey("aria-label") || attrs.ContainsKey("aria-labelledby") || attrs.ContainsKey("title")
        || attrs.ContainsKey("aria-hidden") && attrs["aria-hidden"] == "true";

    private static bool IsLabelled(Dictionary<string, string> attrs, HashSet<string> labelTargets) =>
        attrs.TryGetValue("id", out var id) && labelTargets.Contains(id)
        || attrs.TryGetValue("asp-for", out var aspFor) && labelTargets.Contains("asp:" + aspFor)
        || attrs.TryGetValue("@bind-Value", out var bound) && labelTargets.Contains("bind:" + bound);

    /// <summary>The character ranges of <c>&lt;label&gt;…&lt;/label&gt;</c> elements.</summary>
    private static List<(int Start, int End)> LabelSpans(string text)
    {
        var spans = new List<(int, int)>();
        foreach (Match open in Labels().Matches(text))
        {
            var close = text.IndexOf("</label", open.Index + open.Length, StringComparison.OrdinalIgnoreCase);
            if (close > 0) spans.Add((open.Index, close));
        }
        return spans;
    }

    /// <summary>The ids that a <c>&lt;label for&gt;</c> points to, and the models a <c>&lt;label asp-for&gt;</c> labels.</summary>
    private static HashSet<string> LabelTargets(string text)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Labels().Matches(text))
        {
            var attrs = ParseAttributes(m.Groups["attrs"].Value);
            if (attrs.TryGetValue("for", out var f) && f.Length > 0) targets.Add(f);
            if (attrs.TryGetValue("asp-for", out var a) && a.Length > 0) targets.Add("asp:" + a);
        }
        return targets;
    }

    /// <summary>
    /// True when the element has something between its tags: text, an expression (<c>@x</c>) or a child that is not an empty icon.
    /// Anything with <c>@</c> counts, since the scan cannot evaluate Razor.
    /// </summary>
    private static bool HasContent(string text, Match open, string tag)
    {
        var start = open.Index + open.Length;
        var close = text.IndexOf("</" + tag, start, StringComparison.OrdinalIgnoreCase);
        if (close < 0) return true;
        var inner = text[start..close];
        if (inner.Contains('@')) return true;
        var plain = Regex.Replace(inner, @"<[^>]*>", " ").Replace("&nbsp;", " ");
        return plain.Any(c => !char.IsWhiteSpace(c));
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private static string Snippet(string markup, Match m)
    {
        var raw = markup.Substring(m.Index, Math.Min(m.Length, markup.Length - m.Index));
        var oneLine = Regex.Replace(raw, @"\s+", " ").Trim();
        return oneLine.Length <= 100 ? oneLine : oneLine[..97] + "...";
    }

    // ── Reports ───────────────────────────────────────────────────────

    public static AuditRule RuleOf(string id) => Rules.First(r => r.Id == id);

    public static IReadOnlyList<AuditIssue> ForLevel(IEnumerable<AuditIssue> issues, string level) =>
        [.. issues.Where(i => LevelRank(RuleOf(i.Rule).Level) <= LevelRank(level))];

    public static int LevelRank(string level) => level.ToUpperInvariant() switch
    {
        "A" => 1, "AA" => 2, "AAA" => 3,
        _ => throw new ArgumentException($"Unknown level '{level}'. Choose A, AA or AAA."),
    };

    public static string Report(IReadOnlyList<AuditIssue> issues, int files, string level, string format) => format.ToLowerInvariant() switch
    {
        "json" => ToJson(issues, files, level),
        "markdown" or "md" => ToMarkdown(issues, files, level),
        "html" => ToHtml(issues, files, level),
        _ => throw new ArgumentException($"Unknown format '{format}'. Choose json, markdown or html."),
    };

    public static string ExtensionOf(string format) => format.ToLowerInvariant() switch { "markdown" or "md" => "md", var f => f };

    private const string Scope = "Static markup scan of Razor/HTML source. It does not check colour contrast, focus order or content in the running page.";

    private static string ToJson(IReadOnlyList<AuditIssue> issues, int files, string level) =>
        JsonSerializer.Serialize(new
        {
            level,
            scope = Scope,
            filesScanned = files,
            issueCount = issues.Count,
            issues = issues.Select(i => new
            {
                rule = i.Rule, criterion = RuleOf(i.Rule).Criterion, wcagLevel = RuleOf(i.Rule).Level,
                title = RuleOf(i.Rule).Title, file = i.File, line = i.Line, snippet = i.Snippet,
            }),
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n";

    private static string ToMarkdown(IReadOnlyList<AuditIssue> issues, int files, string level)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Accessibility audit").AppendLine();
        sb.AppendLine($"Level {level} · {files} files scanned · **{issues.Count} issue{(issues.Count == 1 ? "" : "s")}**").AppendLine();
        sb.AppendLine($"_{Scope}_").AppendLine();
        foreach (var group in issues.GroupBy(i => i.Rule).OrderBy(g => g.Key))
        {
            var rule = RuleOf(group.Key);
            sb.AppendLine($"## {rule.Id} · {rule.Title} (WCAG {rule.Criterion}, level {rule.Level})").AppendLine();
            sb.AppendLine(rule.Advice).AppendLine();
            foreach (var i in group)
                sb.AppendLine($"- `{i.File}:{i.Line}` — `{i.Snippet.Replace("`", "'")}`");
            sb.AppendLine();
        }
        if (issues.Count == 0) sb.AppendLine("No issues found.");
        return sb.ToString();
    }

    private static string ToHtml(IReadOnlyList<AuditIssue> issues, int files, string level)
    {
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Accessibility audit</title>");
        sb.AppendLine("<style>body{font:15px/1.5 system-ui;max-width:56rem;margin:2rem auto;padding:0 1rem}code{background:#f3f3f3;padding:0 .25rem}</style></head><body>");
        sb.AppendLine("<h1>Accessibility audit</h1>");
        sb.AppendLine($"<p>Level {E(level)} · {files} files scanned · <strong>{issues.Count} issue{(issues.Count == 1 ? "" : "s")}</strong></p>");
        sb.AppendLine($"<p><em>{E(Scope)}</em></p>");
        foreach (var group in issues.GroupBy(i => i.Rule).OrderBy(g => g.Key))
        {
            var rule = RuleOf(group.Key);
            sb.AppendLine($"<h2>{E(rule.Id)} · {E(rule.Title)} <small>(WCAG {E(rule.Criterion)}, level {E(rule.Level)})</small></h2><p>{E(rule.Advice)}</p><ul>");
            foreach (var i in group)
                sb.AppendLine($"<li><code>{E(i.File)}:{i.Line}</code> — <code>{E(i.Snippet)}</code></li>");
            sb.AppendLine("</ul>");
        }
        if (issues.Count == 0) sb.AppendLine("<p>No issues found.</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
