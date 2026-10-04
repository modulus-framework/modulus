using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Modulus.Analyzers;

/// <summary>
/// MOD0002: a classified value written through an ordinary log call. Log redaction (<c>AddModulusRedaction</c>) only applies
/// to source-generated logging, so <c>logger.LogInformation("{Email}", user.Email)</c> writes the e-mail address as is when
/// <c>Email</c> carries <c>[PersonalInformation]</c>, <c>[ProtectedPersonalData]</c> or any other data-classification attribute.
/// Reports a classified property, field or parameter passed (directly, boxed or inside an interpolated string) to an
/// <c>ILogger</c> extension method (<c>Log</c>, <c>LogInformation</c>, <c>BeginScope</c>, ...). <c>[InternalData]</c> is not
/// redacted, so it is not reported. Only active when <c>Microsoft.Extensions.Compliance.Abstractions</c> resolves.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClassifiedLogArgumentAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic id.</summary>
    public const string DiagnosticId = "MOD0002";

    private const string ClassificationAttributeTypeName = "Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute";
    private const string LoggerExtensionsTypeName = "Microsoft.Extensions.Logging.LoggerExtensions";
    private const string InternalDataTypeName = "Modulus.Core.Abstractions.Compliance.InternalDataAttribute";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        title: "Classified value written through an unredacted log call",
        messageFormat: "'{0}' is classified ({1}) but this log call is not redacted; log it through a [LoggerMessage] method whose parameter carries the classification",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Log redaction applies to source-generated logging only. A classified value passed to LogInformation, " +
                     "LogWarning, BeginScope and the like reaches every log sink in clear text.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var classification = start.Compilation.GetTypeByMetadataName(ClassificationAttributeTypeName);
            var loggerExtensions = start.Compilation.GetTypeByMetadataName(LoggerExtensionsTypeName);
            if (classification is null || loggerExtensions is null)
                return;

            var internalData = start.Compilation.GetTypeByMetadataName(InternalDataTypeName);
            start.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, classification, loggerExtensions, internalData), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context, INamedTypeSymbol classification, INamedTypeSymbol loggerExtensions, INamedTypeSymbol? internalData)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, loggerExtensions))
            return;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
                continue; // the ILogger itself

            foreach (var value in Values(argument.Value))
            {
                if (Classified(value, classification, internalData) is { } found)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        s_rule, value.Syntax.GetLocation(), found.Name, ShortName(found.Attribute)));
                }
            }
        }
    }

    // The argument itself, the elements of a params array, and the holes of an interpolated string; conversions unwrapped.
    private static IEnumerable<IOperation> Values(IOperation operation)
    {
        operation = Unwrap(operation);
        switch (operation)
        {
            case IArrayCreationOperation { Initializer: { } initializer }:
                foreach (var element in initializer.ElementValues)
                {
                    foreach (var value in Values(element))
                        yield return value;
                }

                break;
            case IInterpolatedStringOperation interpolated:
                foreach (var hole in interpolated.Parts.OfType<IInterpolationOperation>())
                {
                    foreach (var value in Values(hole.Expression))
                        yield return value;
                }

                break;
            default:
                yield return operation;
                break;
        }
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static (string Name, INamedTypeSymbol Attribute)? Classified(
        IOperation value, INamedTypeSymbol classification, INamedTypeSymbol? internalData)
    {
        var symbol = value switch
        {
            IPropertyReferenceOperation p => p.Property,
            IFieldReferenceOperation f => f.Field,
            IParameterReferenceOperation p => (ISymbol)p.Parameter,
            _ => null,
        };
        if (symbol is null)
            return null;

        foreach (var attribute in symbol.GetAttributes())
        {
            var type = attribute.AttributeClass;
            if (type is null || SymbolEqualityComparer.Default.Equals(type, internalData))
                continue;
            if (InheritsFrom(type, classification))
                return (symbol.Name, type);
        }

        return null;
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
