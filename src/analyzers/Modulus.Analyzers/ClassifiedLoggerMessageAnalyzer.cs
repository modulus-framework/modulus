using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Analyzers;

/// <summary>
/// MOD0004: a <c>[LoggerMessage]</c> method with a classified parameter in a project that does not reference
/// <c>Microsoft.Extensions.Telemetry.Abstractions</c>. Log redaction for source-generated logging needs the classification-aware
/// generator that package ships; the built-in logging generator ignores data-classification attributes, so the value is written
/// as is. Reports each classified parameter other than <c>[InternalData]</c>. The check reads the project's references, so a
/// reference that arrives only transitively also counts as present (the warning is then missed, never raised wrongly).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClassifiedLoggerMessageAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic id.</summary>
    public const string DiagnosticId = "MOD0004";

    private const string LoggerMessageAttributeTypeName = "Microsoft.Extensions.Logging.LoggerMessageAttribute";
    private const string ClassificationAttributeTypeName = "Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute";
    private const string InternalDataTypeName = "Modulus.Core.Abstractions.Compliance.InternalDataAttribute";
    private const string TelemetryAssemblyName = "Microsoft.Extensions.Telemetry.Abstractions";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        title: "Classified logger parameter is not redacted",
        messageFormat: "'{0}' is classified ({1}) but this [LoggerMessage] method is generated without log redaction; reference Microsoft.Extensions.Telemetry.Abstractions in this project so its classification-aware generator applies",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The built-in logging generator ignores data-classification attributes. Without Microsoft.Extensions.Telemetry.Abstractions " +
                     "in the project, a classified [LoggerMessage] parameter is written to every log sink in clear text.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var loggerMessage = start.Compilation.GetTypeByMetadataName(LoggerMessageAttributeTypeName);
            var classification = start.Compilation.GetTypeByMetadataName(ClassificationAttributeTypeName);
            if (loggerMessage is null || classification is null)
                return;

            // The classification-aware generator is in this project only when the package is referenced directly.
            if (start.Compilation.ReferencedAssemblyNames.Any(name => string.Equals(name.Name, TelemetryAssemblyName, System.StringComparison.Ordinal)))
                return;

            var internalData = start.Compilation.GetTypeByMetadataName(InternalDataTypeName);
            start.RegisterSymbolAction(ctx => AnalyzeMethod(ctx, loggerMessage, classification, internalData), SymbolKind.Method);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, INamedTypeSymbol loggerMessage, INamedTypeSymbol classification, INamedTypeSymbol? internalData)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, loggerMessage)))
            return;

        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                var type = attribute.AttributeClass;
                if (type is null || SymbolEqualityComparer.Default.Equals(type, internalData) || !InheritsFrom(type, classification))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(s_rule, parameter.Locations.FirstOrDefault(), parameter.Name, ShortName(type)));
                break;
            }
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }

    private static string ShortName(INamedTypeSymbol attribute)
        => attribute.Name.EndsWith("Attribute", System.StringComparison.Ordinal)
            ? attribute.Name.Substring(0, attribute.Name.Length - "Attribute".Length)
            : attribute.Name;
}
