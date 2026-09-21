using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// <c>Ux.DryRun</c>, <c>Ux.Force</c> and <c>Ux.Quiet</c> are process-wide statics that <c>UxTests</c> flips, and the
/// scaffolders write through <c>Ux.WriteFile</c>, which honours <c>DryRun</c>. A test that scaffolds files while
/// <c>UxTests</c> runs would randomly find nothing written, so those tests share this collection and run alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UxStateCollection
{
    public const string Name = "Ux static state";
}
