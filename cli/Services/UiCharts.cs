namespace Modulus.Cli.Services;

/// <summary>A chart page the template package ships, built on the framework's chart components with sample data.</summary>
internal sealed record UiChart(string Key)
{
    public UiAuthPage Page => new($"chart-{Key}", char.ToUpperInvariant(Key[0]) + Key[1..], $"/charts/{Key}");
}

internal static class UiCharts
{
    public const string TypeList = "line, column, donut or heatmap";

    public static readonly IReadOnlyList<UiChart> All = [new("line"), new("column"), new("donut"), new("heatmap")];

    public static UiChart Find(string type) =>
        All.FirstOrDefault(c => c.Key.Equals(type.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown chart type '{type}'. Choose one of: {TypeList}.");
}
