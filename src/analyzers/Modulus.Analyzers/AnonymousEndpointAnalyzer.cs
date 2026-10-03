using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Modulus.Analyzers;

/// <summary>
/// MOD0001: an endpoint opened to anonymous callers without a reason. The startup security guard refuses such an
/// endpoint at boot; this reports it at build time:
/// <list type="bullet">
/// <item>a <c>.AllowAnonymous()</c> chain on an endpoint builder with no <c>LoosenedAttribute</c> metadata (use <c>.Loosen(reason)</c>);</item>
/// <item><c>[AllowAnonymous]</c> on a controller, action or Razor page without <c>[Loosened(reason)]</c>;</item>
/// <item>the parameterless <c>AllowAnonymous()</c> in a REPR endpoint's <c>Configure()</c> (use <c>AllowAnonymous(reason)</c>).</item>
/// </list>
/// Only active in projects that reference Modulus (the <c>LoosenedAttribute</c> type must resolve).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AnonymousEndpointAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic id.</summary>
    public const string DiagnosticId = "MOD0001";

    private const string LoosenedTypeName = "Modulus.Core.Abstractions.Security.LoosenedAttribute";
    private const string AllowAnonymousTypeName = "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";
    private const string ConventionExtensionsTypeName = "Microsoft.AspNetCore.Builder.AuthorizationEndpointConventionBuilderExtensions";
    private const string EndpointBaseTypeName = "Modulus.AspNetCore.Endpoints.EndpointBase";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        title: "Anonymous endpoint without a reason",
        messageFormat: "{0} opens an endpoint to anonymous callers without a reason; use {1}",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every endpoint is closed by default. Opening one is a reviewed decision whose reason travels with the " +
                     "endpoint, so the startup security guard can report it and check it against the loosening allow-list.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var loosened = start.Compilation.GetTypeByMetadataName(LoosenedTypeName);
            if (loosened is null)
                return;

            var allowAnonymous = start.Compilation.GetTypeByMetadataName(AllowAnonymousTypeName);
            var conventions = start.Compilation.GetTypeByMetadataName(ConventionExtensionsTypeName);
            var endpointBase = start.Compilation.GetTypeByMetadataName(EndpointBaseTypeName);

            start.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, loosened, conventions, endpointBase), OperationKind.Invocation);

            if (allowAnonymous is not null)
            {
                start.RegisterSymbolAction(
                    ctx => AnalyzeSymbol(ctx, loosened, allowAnonymous), SymbolKind.NamedType, SymbolKind.Method);
            }
        });
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context, INamedTypeSymbol loosened, INamedTypeSymbol? conventions, INamedTypeSymbol? endpointBase)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "AllowAnonymous")
            return;

        if (endpointBase is not null && method.Parameters.Length == 0 && InheritsFrom(method.ContainingType, endpointBase))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_rule, invocation.Syntax.GetLocation(), "AllowAnonymous()", "AllowAnonymous(reason)"));
            return;
        }

        if (conventions is null || !SymbolEqualityComparer.Default.Equals(method.ContainingType, conventions))
            return;

        if (ChainCarriesLoosening(invocation, loosened))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            s_rule, invocation.Syntax.GetLocation(), ".AllowAnonymous()", ".Loosen(reason)"));
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol loosened, INamedTypeSymbol allowAnonymous)
    {
        var attributes = context.Symbol.GetAttributes();
        var anonymous = attributes.FirstOrDefault(a => InheritsFrom(a.AttributeClass, allowAnonymous));
        if (anonymous is null || attributes.Any(a => InheritsFrom(a.AttributeClass, loosened)))
            return;

        var location = anonymous.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                       ?? context.Symbol.Locations.FirstOrDefault();
        if (location is not null)
            context.ReportDiagnostic(Diagnostic.Create(s_rule, location, "[AllowAnonymous]", "[AllowAnonymous, Loosened(reason)]"));
    }

    /// <summary>Whether the fluent chain around <paramref name="invocation"/> adds <c>LoosenedAttribute</c> metadata.</summary>
    private static bool ChainCarriesLoosening(IInvocationOperation invocation, INamedTypeSymbol loosened)
    {
        // Calls made on the result: a.AllowAnonymous().WithMetadata(new LoosenedAttribute(...)).
        IOperation current = invocation;
        while (Unwrap(current.Parent) is { } parent)
        {
            if (parent is IArgumentOperation { Parent: IInvocationOperation outer } && ReceiverOf(outer) == current)
            {
                if (CreatesLoosening(outer, loosened))
                    return true;
                current = outer;
            }
            else if (parent is IInvocationOperation outerInstance && outerInstance.Instance == current)
            {
                if (CreatesLoosening(outerInstance, loosened))
                    return true;
                current = outerInstance;
            }
            else
            {
                break;
            }
        }

        // Calls made before: a.WithMetadata(new LoosenedAttribute(...)).AllowAnonymous().
        for (var receiver = ReceiverOf(invocation) as IInvocationOperation; receiver is not null; receiver = ReceiverOf(receiver) as IInvocationOperation)
        {
            if (CreatesLoosening(receiver, loosened))
                return true;
        }

        return false;
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Parent;
        return operation;
    }

    private static IOperation? ReceiverOf(IInvocationOperation invocation)
    {
        var receiver = invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length > 0
            ? invocation.Arguments[0].Value
            : invocation.Instance;
        while (receiver is IConversionOperation conversion)
            receiver = conversion.Operand;
        return receiver;
    }

    private static bool CreatesLoosening(IInvocationOperation invocation, INamedTypeSymbol loosened)
        => invocation.TargetMethod.Name is "WithMetadata" or "Loosen"
           && invocation.Arguments
               .Skip(invocation.TargetMethod.IsExtensionMethod ? 1 : 0)
               .SelectMany(a => a.Descendants().Prepend(a.Value))
               .Any(o => (o is IObjectCreationOperation creation && InheritsFrom(creation.Type, loosened))
                         || (invocation.TargetMethod.Name == "Loosen" && o.Type?.SpecialType == SpecialType.System_String));

    private static bool InheritsFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
                return true;
        }

        return false;
    }
}
