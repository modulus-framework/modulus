using Modulus.Cli.Services;

namespace Modulus.Cli.Tests;

/// <summary>
/// The engines whose page set is complete: the page-set tests loop over these. Fluid is left out until its auth, admin, chart and
/// component pages are ported; its own templates are tested as Liquid (see the Fluid template tests).
/// </summary>
internal static class TestEngines
{
    public static readonly string[] Paged = UiScaffold.Engines.Where(e => e != "fluid").ToArray();
}
