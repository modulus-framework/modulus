using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Modulus.Analyzers.Tests;

/// <summary>MOD0004: a classified <c>[LoggerMessage]</c> parameter is reported unless the project references the telemetry package.</summary>
[Trait("Category", "Unit")]
public sealed class ClassifiedLoggerMessageAnalyzerTests
{
    // Minimal stand-ins for the logging, compliance and Modulus types the analyzer recognises by metadata name.
    private const string Stubs = """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }
            public enum LogLevel { Information }
            public sealed class LoggerMessageAttribute : System.Attribute
            {
                public int EventId { get; set; }
                public LogLevel Level { get; set; }
                public string? Message { get; set; }
            }
        }
        namespace Microsoft.Extensions.Compliance.Classification
        {
            public abstract class DataClassificationAttribute : System.Attribute { }
        }
        namespace Modulus.Core.Abstractions.Compliance
        {
            using Microsoft.Extensions.Compliance.Classification;
            public sealed class PersonalInformationAttribute : DataClassificationAttribute { }
            public sealed class InternalDataAttribute : DataClassificationAttribute { }
        }
        """;

    private const string Usings = """
        using Microsoft.Extensions.Logging;
        using Modulus.Core.Abstractions.Compliance;
        """;

    // A stand-in for the telemetry package: only its assembly name matters to the analyzer.
    private const string TelemetryStub = "namespace Microsoft.Extensions.Telemetry.Logging { public sealed class LogPropertiesAttribute : System.Attribute { } }";

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, bool telemetryReferenced)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")),
        };
        if (telemetryReferenced)
        {
            var telemetry = CSharpCompilation.Create(
                "Microsoft.Extensions.Telemetry.Abstractions",
                [CSharpSyntaxTree.ParseText(TelemetryStub)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            references.Add(telemetry.ToMetadataReference());
        }

        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(Stubs), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return await compilation.WithAnalyzers([new ClassifiedLoggerMessageAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    private const string Source = Usings + """
        public static partial class Log
        {
            [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Customer {Email}")]
            static partial void Customer(ILogger logger, [PersonalInformation] string email);

            [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Branch {Branch}")]
            static partial void Branch(ILogger logger, [InternalData] string branch);

            [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Code {Code}")]
            static partial void Code(ILogger logger, string code);
        }
        """;

    [Fact]
    public async Task A_classified_parameter_without_the_telemetry_package_is_reported_once()
    {
        var diagnostics = await AnalyzeAsync(Source, telemetryReferenced: false);

        diagnostics.Should().ContainSingle(d => d.Id == "MOD0004");
        var warning = diagnostics.Single(d => d.Id == "MOD0004");
        warning.GetMessage().Should().Contain("'email'").And.Contain("PersonalInformation");
        warning.Location.GetLineSpan().StartLinePosition.Line.Should().Be(Source.Split('\n').ToList().FindIndex(l => l.Contains("[PersonalInformation]")));
    }

    [Fact]
    public async Task With_the_telemetry_package_referenced_the_generator_redacts_and_nothing_is_reported()
    {
        var diagnostics = await AnalyzeAsync(Source, telemetryReferenced: true);

        diagnostics.Should().NotContain(d => d.Id == "MOD0004");
    }

    [Fact]
    public async Task Internal_and_unclassified_parameters_are_not_reported()
    {
        var diagnostics = await AnalyzeAsync(Source, telemetryReferenced: false);

        var messages = diagnostics.Where(d => d.Id == "MOD0004").Select(d => d.GetMessage()).ToList();
        messages.Should().ContainSingle().Which.Should().Contain("'email'");
        messages.Should().NotContain(m => m.Contains("'branch'") || m.Contains("'code'"));
    }
}
