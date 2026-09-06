namespace Modulus.Testing.Tests;

using FluentAssertions;
using Modulus.Testing.Architecture;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ModuleBoundaryRulesTests
{
    [Fact]
    public void FindUnnamedIntegrationEvents_ReturnsEmptyForFramework()
    {
        // All framework integration events should have [IntegrationEventName].
        var unnamed = ModuleBoundaryRules.FindUnnamedIntegrationEvents();
        unnamed.Should().BeEmpty("All integration events must have [IntegrationEventName]");
    }

    [Fact]
    public void FindModuleTypes_ReturnsModuleImplementations()
    {
        // The method should be callable and return a read-only list (possibly empty in minimal test contexts).
        // Actual modules are discovered from the loaded app domain.
        var modules = ModuleBoundaryRules.FindModuleTypes();
        modules.Should().NotBeNull("FindModuleTypes should return a list");
        modules.Should().BeAssignableTo<IReadOnlyList<Type>>("Result should be a read-only list of types");
    }

    [Fact]
    public void FindCrossModuleDomainTypeUsage_ReturnsEmptyForFramework()
    {
        // The framework should not have cross-module domain type leakage.
        var violations = ModuleBoundaryRules.FindCrossModuleDomainTypeUsage();

        // Filter to framework-only violations (not test infrastructure noise).
        var frameworkViolations = violations
            .Where(v => v.ViolatingType.Namespace?.StartsWith("Modulus") == true &&
                        v.DomainType.Namespace?.StartsWith("Modulus") == true)
            .ToList();

        frameworkViolations.Should().BeEmpty(
            "Framework modules should not reference other modules' domain types");
    }

    [Fact]
    public void FindCrossModuleDbContextUsage_ReturnsEmptyForFramework()
    {
        // DbContext should not leak across module boundaries.
        var violations = ModuleBoundaryRules.FindCrossModuleDbContextUsage();

        var frameworkViolations = violations
            .Where(v => v.ViolatingType.Namespace?.StartsWith("Modulus") == true &&
                        v.DbContextType.Namespace?.StartsWith("Modulus") == true)
            .ToList();

        frameworkViolations.Should().BeEmpty(
            "DbContext should not be referenced across module boundaries");
    }
}
