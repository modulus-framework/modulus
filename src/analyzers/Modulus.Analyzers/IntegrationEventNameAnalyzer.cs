using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Analyzers;

/// <summary>
/// MOD0003: an integration event's stable name must be declared once, in a well-formed
/// <c>[IntegrationEvent&lt;TModule&gt;]</c> (or the legacy <c>[IntegrationEventName]</c>):
/// <list type="bullet">
/// <item>a concrete event type with no <c>[IntegrationEventName]</c> falls back to its CLR name, which a rename orphans;</item>
/// <item>a hand-typed name repeated in <c>IntegrationEventBase("...")</c> can drift from the attribute (inherit <c>IntegrationEventBase</c> with no argument);</item>
/// <item>a legacy single-string name must read <c>module.event.vN</c> (lower-case kebab parts); prefer <c>[IntegrationEvent&lt;TModule&gt;]</c>, which has no literal.</item>
/// </list>
/// Only active in projects that reference <c>Modulus.Events</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IntegrationEventNameAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic id.</summary>
    public const string DiagnosticId = "MOD0003";

    private const string BaseTypeName = "Modulus.Events.Abstractions.IntegrationEventBase";
    private const string AttributeTypeName = "Modulus.Events.Abstractions.IntegrationEventNameAttribute";
    private const string DerivedAttributeTypeName = "Modulus.Events.Abstractions.IntegrationEventAttributeBase";

    private static readonly Regex s_wellFormed = new(@"^[a-z0-9-]+\.[a-z0-9-]+\.v[1-9][0-9]*$", RegexOptions.Compiled);

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        title: "Integration event name is not declared once and well-formed",
        messageFormat: "{0}",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The event name is the wire and outbox contract. Declare it once with " +
                     "[IntegrationEvent<TModule>] (derived from types, no string literal) and inherit IntegrationEventBase without an argument.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var eventBase = start.Compilation.GetTypeByMetadataName(BaseTypeName);
            var attribute = start.Compilation.GetTypeByMetadataName(AttributeTypeName);
            var derivedAttribute = start.Compilation.GetTypeByMetadataName(DerivedAttributeTypeName);
            if (eventBase is null || attribute is null || derivedAttribute is null)
                return;

            start.RegisterSymbolAction(ctx => AnalyzeType(ctx, eventBase, attribute, derivedAttribute), SymbolKind.NamedType);
            start.RegisterSyntaxNodeAction(ctx => AnalyzeBaseArgument(ctx, eventBase), SyntaxKind.PrimaryConstructorBaseType);
        });
    }

    private static void AnalyzeType(
        SymbolAnalysisContext context, INamedTypeSymbol eventBase, INamedTypeSymbol attribute, INamedTypeSymbol derivedAttribute)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsAbstract || type.TypeKind is not (TypeKind.Class) || !DerivesFrom(type, eventBase))
            return;

        var attributes = type.GetAttributes();
        if (attributes.Any(a => a.AttributeClass is { } c && DerivesFrom(c, derivedAttribute)))
            return;

        var declared = attributes.FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
        if (declared is null)
        {
            Report(context, type.Locations.FirstOrDefault(),
                $"'{type.Name}' is an integration event with no [IntegrationEvent<TModule>] (or legacy [IntegrationEventName]); its name would fall back to the CLR type name and break on a rename");
            return;
        }

        if (declared.ConstructorArguments.Length == 1
            && declared.ConstructorArguments[0].Value is string legacy
            && !s_wellFormed.IsMatch(legacy))
        {
            var location = declared.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                           ?? type.Locations.FirstOrDefault();
            Report(context, location,
                $"Event name '{legacy}' must read module.event.vN (lower-case kebab parts); prefer [IntegrationEvent<YourModuleArea>], which derives the name from types");
        }
    }

    private static void AnalyzeBaseArgument(SyntaxNodeAnalysisContext context, INamedTypeSymbol eventBase)
    {
        var node = (PrimaryConstructorBaseTypeSyntax)context.Node;
        if (node.ArgumentList.Arguments.Count == 0)
            return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(node.Parent!.Parent!, context.CancellationToken) as INamedTypeSymbol;
        if (symbol is null || !DerivesFrom(symbol, eventBase)
            || !SymbolEqualityComparer.Default.Equals(symbol.BaseType, eventBase))
            return;

        Report(context.ReportDiagnostic, node.ArgumentList.GetLocation(),
            "Do not repeat the event name in IntegrationEventBase(...); declare it on [IntegrationEvent<TModule>] and inherit IntegrationEventBase with no argument");
    }

    private static void Report(SymbolAnalysisContext context, Location? location, string message)
    {
        if (location is not null)
            context.ReportDiagnostic(Diagnostic.Create(s_rule, location, message));
    }

    private static void Report(System.Action<Diagnostic> report, Location location, string message)
        => report(Diagnostic.Create(s_rule, location, message));

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }
}
