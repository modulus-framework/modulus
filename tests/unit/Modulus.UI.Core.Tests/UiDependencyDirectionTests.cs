using FluentAssertions;
using System.Reflection;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// Dependency direction (§1): UI packages depend on Modulus, never the reverse.
/// Every non-UI <c>Modulus.*</c> assembly beside the test host must not reference
/// any <c>Modulus.UI*</c> assembly.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UiDependencyDirectionTests
{
    [Fact]
    public void Core_packages_must_not_reference_UI()
    {
        var offenders = new List<string>();
        var dir = AppContext.BaseDirectory;

        foreach (var dll in Directory.GetFiles(dir, "Modulus.*.dll"))
        {
            var simpleName = Path.GetFileNameWithoutExtension(dll);
            if (simpleName.StartsWith("Modulus.UI", StringComparison.Ordinal)
                || simpleName.Contains(".Tests", StringComparison.Ordinal))
                continue;

            Assembly candidate;
            try
            {
                candidate = Assembly.LoadFrom(dll);
            }
            catch
            {
                continue;
            }

            foreach (var reference in candidate.GetReferencedAssemblies())
            {
                if (reference.Name is not null
                    && reference.Name.StartsWith("Modulus.UI", StringComparison.Ordinal))
                    offenders.Add($"{simpleName} -> {reference.Name}");
            }
        }

        offenders.Should().BeEmpty(
            "UI packages depend on Modulus, never the reverse (design guide §1)");
    }
}
