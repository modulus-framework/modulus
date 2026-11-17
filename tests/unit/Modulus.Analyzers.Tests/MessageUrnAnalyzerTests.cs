using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Modulus.Analyzers.Tests;

[Trait("Category", "Unit")]
public sealed class MessageUrnAnalyzerTests
{
    private const string Stubs = """
        namespace Modulus.Messaging
        {
            public interface IBus
            {
                System.Threading.Tasks.Task Send<TMessage>(System.Uri address, TMessage message) where TMessage : class;
                System.Threading.Tasks.Task Publish<TMessage>(TMessage message) where TMessage : class;
            }
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class MessageUrnAttribute : System.Attribute
            {
                public MessageUrnAttribute(string urn) { }
            }
        }
        """;

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }

    private sealed class Provider(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        private readonly Options _global = new(values);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(new Dictionary<string, string>());
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, bool require)
    {
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(Stubs), CSharpSyntaxTree.ParseText("using System;\nusing Modulus.Messaging;\n" + source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
             MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var values = new Dictionary<string, string>();
        if (require)
        {
            values[MessageUrnAnalyzer.RequireProperty] = "true";
        }

        var options = new AnalyzerOptions([], new Provider(values));
        return await compilation.WithAnalyzers([new MessageUrnAnalyzer()], options).GetAnalyzerDiagnosticsAsync();
    }

    private const string Bus = """
        public sealed class Sender(IBus bus)
        {
            public Task Run() => bus.Publish(new OrderSubmitted(1));
        }
        """;

    [Fact]
    public async Task Nothing_is_reported_unless_the_project_asks_for_it()
    {
        var diagnostics = await AnalyzeAsync("public sealed record OrderSubmitted(int Id);\n" + Bus, require: false);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task A_sent_message_without_a_urn_is_reported_when_required()
    {
        var diagnostics = await AnalyzeAsync("public sealed record OrderSubmitted(int Id);\n" + Bus, require: true);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be(MessageUrnAnalyzer.DiagnosticId);
        diagnostics.Single().GetMessage().Should().Contain("OrderSubmitted");
    }

    [Fact]
    public async Task A_message_with_a_urn_is_clean()
    {
        var diagnostics = await AnalyzeAsync("[MessageUrn(\"urn:orders:order-submitted:v1\")]\npublic sealed record OrderSubmitted(int Id);\n" + Bus, require: true);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task A_type_that_is_never_sent_is_not_reported()
    {
        var diagnostics = await AnalyzeAsync("public sealed record Unused(int Id);\n" + Bus, require: true);

        diagnostics.Should().BeEmpty();
    }
}
