using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Modulus.Analyzers.Tests;

[Trait("Category", "Unit")]
public sealed class ClassifiedLogArgumentAnalyzerTests
{
    // Minimal stand-ins for the logging, compliance and Modulus types the analyzer recognises by metadata name.
    private const string Stubs = """
        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger { }
            public static class LoggerExtensions
            {
                public static void LogInformation(this ILogger logger, string? message, params object?[] args) { }
                public static void LogWarning(this ILogger logger, System.Exception? exception, string? message, params object?[] args) { }
                public static System.IDisposable? BeginScope(this ILogger logger, string messageFormat, params object?[] args) => null;
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
            public sealed class SecretDataAttribute : DataClassificationAttribute { }
            public sealed class InternalDataAttribute : DataClassificationAttribute { }
        }
        namespace Modulus.Core.Abstractions.DataProtection
        {
            public sealed class ProtectedPersonalDataAttribute : Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute { }
        }
        """;

    private const string Usings = """
        using Microsoft.Extensions.Logging;
        using Modulus.Core.Abstractions.Compliance;
        using Modulus.Core.Abstractions.DataProtection;
        """;

    private const string Customer = """
        public sealed class Customer
        {
            public string Code { get; set; } = "";
            [InternalData] public string Branch { get; set; } = "";
            [ProtectedPersonalData] public string Email { get; set; } = "";
            [PersonalInformation] public string Name = "";
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, bool withCompliance = true)
    {
        var stubs = withCompliance ? Stubs : Stubs.Replace("namespace Microsoft.Extensions.Compliance.Classification", "namespace Elsewhere.Classification")
            .Replace("Microsoft.Extensions.Compliance.Classification", "Elsewhere.Classification");
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return await compilation.WithAnalyzers([new ClassifiedLogArgumentAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task Classified_members_passed_to_log_calls_are_reported()
    {
        var diagnostics = await AnalyzeAsync(Usings + Customer + """
            static class P
            {
                static void M(ILogger logger, Customer c, System.Exception e)
                {
                    logger.LogInformation("Created {Email}", c.Email);
                    logger.LogWarning(e, "Renamed to {Name}", c.Name);
                    logger.LogInformation($"Hello {c.Name}");
                    using var scope = logger.BeginScope("Customer {Email}", c.Email);
                }
            }
            """);

        diagnostics.Should().HaveCount(4).And.OnlyContain(d => d.Id == "MOD0002");
        diagnostics[0].GetMessage().Should().Contain("'Email'").And.Contain("ProtectedPersonalData").And.Contain("[LoggerMessage]");
        diagnostics[1].GetMessage().Should().Contain("'Name'").And.Contain("PersonalInformation");
    }

    [Fact]
    public async Task A_classified_parameter_is_reported()
    {
        var diagnostics = await AnalyzeAsync(Usings + """
            static class P
            {
                static void M(ILogger logger, [SecretData] string password) => logger.LogInformation("Password {P}", password);
            }
            """);

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain("SecretData");
    }

    [Fact]
    public async Task Unclassified_and_internal_values_are_not_reported()
    {
        var diagnostics = await AnalyzeAsync(Usings + Customer + """
            static class P
            {
                static void M(ILogger logger, Customer c, System.Guid id)
                    => logger.LogInformation("Customer {Id} {Code} {Branch}", id, c.Code, c.Branch);
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_the_compliance_abstractions_the_analyzer_is_silent()
    {
        var diagnostics = await AnalyzeAsync(Usings + Customer + """
            static class P { static void M(ILogger logger, Customer c) => logger.LogInformation("{Email}", c.Email); }
            """, withCompliance: false);

        diagnostics.Should().BeEmpty();
    }
}
