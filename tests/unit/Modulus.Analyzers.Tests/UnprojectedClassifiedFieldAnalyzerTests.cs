using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Modulus.Analyzers.Tests;

/// <summary>MOD0005: a classified entity field copied to a type that does not declare <c>[ProjectionOf]</c> is reported.</summary>
[Trait("Category", "Unit")]
public sealed class UnprojectedClassifiedFieldAnalyzerTests
{
    // Stand-ins for the Modulus types the analyzer recognises by metadata name.
    private const string Stubs = """
        namespace Modulus.Core.Abstractions.Entities
        {
            public enum FieldClassification { Public = 0, Restricted = 3 }
            public sealed class ClassifiedAttribute(FieldClassification classification) : System.Attribute
            {
                public FieldClassification Classification { get; } = classification;
            }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ProjectionOfAttribute(System.Type entityType) : System.Attribute
            {
                public System.Type EntityType { get; } = entityType;
            }
        }
        """;

    private const string Usings = """
        using Modulus.Core.Abstractions.Entities;
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(Stubs), CSharpSyntaxTree.ParseText(Usings + source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var diagnostics = await compilation.WithAnalyzers([new UnprojectedClassifiedFieldAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        return [.. diagnostics.Where(d => d.Id == UnprojectedClassifiedFieldAnalyzer.DiagnosticId)];
    }

    private const string Entity = """
        public sealed class Candidate
        {
            public string Name { get; set; } = "";
            [Classified(FieldClassification.Restricted)]
            public decimal Salary { get; set; }
        }
        """;

    [Fact]
    public async Task A_copied_classified_field_on_an_undeclared_type_is_reported()
    {
        var diagnostics = await AnalyzeAsync(Entity + """
            public sealed class CandidateSummary
            {
                public string Name { get; set; } = "";
                public decimal Salary { get; set; }
            }
            """);

        diagnostics.Should().HaveCount(1);
        diagnostics[0].GetMessage().Should().Contain("Salary").And.Contain("Candidate");
    }

    [Fact]
    public async Task A_type_that_declares_ProjectionOf_is_not_reported()
    {
        var diagnostics = await AnalyzeAsync(Entity + """
            [ProjectionOf(typeof(Candidate))]
            public sealed class CandidateSummary
            {
                public string Name { get; set; } = "";
                public decimal Salary { get; set; }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task A_property_classified_explicitly_is_not_reported()
    {
        var diagnostics = await AnalyzeAsync(Entity + """
            public sealed class CandidateSummary
            {
                public string Name { get; set; } = "";
                [Classified(FieldClassification.Restricted)]
                public decimal Salary { get; set; }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task A_field_with_the_same_name_but_another_type_is_not_reported()
    {
        var diagnostics = await AnalyzeAsync(Entity + """
            public sealed class CandidateSummary
            {
                public string Salary { get; set; } = "";
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task The_entity_itself_is_not_reported()
    {
        var diagnostics = await AnalyzeAsync(Entity);

        diagnostics.Should().BeEmpty();
    }
}
