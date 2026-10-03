using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Modulus.Analyzers.Tests;

[Trait("Category", "Unit")]
public sealed class AnonymousEndpointAnalyzerTests
{
    // Minimal stand-ins for the ASP.NET Core and Modulus types the analyzer recognises by metadata name.
    private const string Stubs = """
        namespace Microsoft.AspNetCore.Builder
        {
            public interface IEndpointConventionBuilder { }
            public sealed class RouteHandlerBuilder : IEndpointConventionBuilder { }
            public static class AuthorizationEndpointConventionBuilderExtensions
            {
                public static TBuilder AllowAnonymous<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder => builder;
                public static TBuilder RequireAuthorization<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder => builder;
            }
            public static class RoutingEndpointConventionBuilderExtensions
            {
                public static TBuilder WithMetadata<TBuilder>(this TBuilder builder, params object[] items) where TBuilder : IEndpointConventionBuilder => builder;
            }
            public static class App
            {
                public static RouteHandlerBuilder MapGet(string pattern) => new();
            }
        }
        namespace Microsoft.AspNetCore.Authorization
        {
            public class AllowAnonymousAttribute : System.Attribute { }
        }
        namespace Modulus.Core.Abstractions.Security
        {
            public sealed class LoosenedAttribute : System.Attribute
            {
                public LoosenedAttribute(string reason) { }
                public string? Ticket { get; set; }
            }
        }
        namespace Modulus.AspNetCore.Endpoints
        {
            public abstract class EndpointBase
            {
                protected void AllowAnonymous() { }
                protected void AllowAnonymous(string reason, string? ticket = null) { }
                public abstract void Configure();
            }
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, bool withModulus = true)
    {
        var stubs = withModulus ? Stubs : Stubs.Replace("public sealed class LoosenedAttribute", "public sealed class NotLoosenedAttribute")
            .Replace("public LoosenedAttribute(", "public NotLoosenedAttribute(");
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
             MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return await compilation.WithAnalyzers([new AnonymousEndpointAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    private const string Usings = """
        using Microsoft.AspNetCore.Authorization;
        using Microsoft.AspNetCore.Builder;
        using Modulus.Core.Abstractions.Security;
        using Modulus.AspNetCore.Endpoints;
        """;

    [Fact]
    public async Task A_bare_AllowAnonymous_chain_is_reported()
    {
        var diagnostics = await AnalyzeAsync(Usings + """
            static class P { static void M() => App.MapGet("/x").AllowAnonymous(); }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("MOD0001");
        diagnostics[0].GetMessage().Should().Contain(".Loosen(reason)");
    }

    [Theory]
    [InlineData("""App.MapGet("/x").AllowAnonymous().WithMetadata(new LoosenedAttribute("probe"))""")]
    [InlineData("""App.MapGet("/x").WithMetadata(new LoosenedAttribute("probe") { Ticket = "T-1" }).AllowAnonymous()""")]
    [InlineData("""App.MapGet("/x").RequireAuthorization()""")]
    public async Task A_reasoned_or_closed_chain_is_not_reported(string chain)
    {
        var diagnostics = await AnalyzeAsync(Usings + $$"""
            static class P { static void M() => {{chain}}; }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task AllowAnonymous_attribute_needs_a_Loosened_attribute()
    {
        var diagnostics = await AnalyzeAsync(Usings + """
            [AllowAnonymous] public sealed class OpenPage { }
            [AllowAnonymous, Loosened("Sign-in page")] public sealed class ReasonedPage { }
            public sealed class Controller
            {
                [AllowAnonymous] public void Open() { }
                [AllowAnonymous][Loosened("Webhook, HMAC-verified")] public void Reasoned() { }
            }
            """);

        diagnostics.Should().HaveCount(2).And.OnlyContain(d => d.Id == "MOD0001");
        diagnostics.Select(d => d.Location.GetLineSpan().StartLinePosition.Line).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task The_parameterless_REPR_AllowAnonymous_is_reported_and_the_reasoned_one_is_not()
    {
        var diagnostics = await AnalyzeAsync(Usings + """
            public sealed class Open : EndpointBase { public override void Configure() => AllowAnonymous(); }
            public sealed class Reasoned : EndpointBase { public override void Configure() => AllowAnonymous("Public catalog"); }
            """);

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain("AllowAnonymous(reason)");
    }

    [Fact]
    public async Task Projects_without_Modulus_are_left_alone()
    {
        var diagnostics = await AnalyzeAsync("""
            using Microsoft.AspNetCore.Builder;
            static class P { static void M() => App.MapGet("/x").AllowAnonymous(); }
            """, withModulus: false);

        diagnostics.Should().BeEmpty();
    }
}
