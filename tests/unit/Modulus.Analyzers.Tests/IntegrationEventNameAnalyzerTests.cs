using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Modulus.Analyzers.Tests;

[Trait("Category", "Unit")]
public sealed class IntegrationEventNameAnalyzerTests
{
    private const string Stubs = """
        namespace Modulus.Events.Abstractions
        {
            public abstract record IntegrationEventBase(string? EventType) { protected IntegrationEventBase() : this((string?)null) { } }
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class IntegrationEventNameAttribute : System.Attribute
            {
                public IntegrationEventNameAttribute(string name) { }
                public IntegrationEventNameAttribute(string module, string eventName, int version = 1) { }
            }
        }
        using Modulus.Events.Abstractions;
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        // The stubs' trailing using is a typo guard: keep them in a separate tree from the probe source.
        var stubs = Stubs[..Stubs.IndexOf("using Modulus", StringComparison.Ordinal)];
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText("using Modulus.Events.Abstractions;\n" + source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return await compilation.WithAnalyzers([new IntegrationEventNameAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task A_structured_name_with_a_parameterless_base_is_clean()
    {
        var diagnostics = await AnalyzeAsync("""
            [IntegrationEventName("payments", "subscription-purchased")]
            public sealed record Purchased(System.Guid Id) : IntegrationEventBase;
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_without_a_name_is_reported()
    {
        var diagnostics = await AnalyzeAsync("public sealed record Purchased(System.Guid Id) : IntegrationEventBase;");

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MOD0003");
    }

    [Fact]
    public async Task A_name_repeated_in_the_base_constructor_is_reported()
    {
        var diagnostics = await AnalyzeAsync("""
            [IntegrationEventName("payments.purchased.v1")]
            public sealed record Purchased(System.Guid Id) : IntegrationEventBase("payments.purchased.v1");
            """);

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain("Do not repeat");
    }

    [Theory]
    [InlineData("Payments.Purchased")]
    [InlineData("payments.purchased")]
    [InlineData("payments.purchased.v0")]
    public async Task A_malformed_single_string_name_is_reported(string name)
    {
        var diagnostics = await AnalyzeAsync($$"""
            [IntegrationEventName("{{name}}")]
            public sealed record Purchased(System.Guid Id) : IntegrationEventBase;
            """);

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain("module.event.vN");
    }

    [Fact]
    public async Task A_well_formed_legacy_name_is_accepted()
    {
        var diagnostics = await AnalyzeAsync("""
            [IntegrationEventName("payments.purchased.v2")]
            public sealed record Purchased(System.Guid Id) : IntegrationEventBase;
            """);

        diagnostics.Should().BeEmpty();
    }
}
