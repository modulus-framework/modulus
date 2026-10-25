using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Analyzers;

/// <summary>
/// MOD0005: a property with the name and type of a classified entity field, on a type that does not declare
/// <c>[ProjectionOf]</c> for that entity. Field security applies an entity's classifications to a projection only through
/// <c>[ProjectionOf]</c>, so such a copy is returned unmasked (F-15). The rule matches by name and type within the
/// compilation, so a same-named unrelated field can be flagged; declare <c>[ProjectionOf]</c> on the type, or classify the
/// property explicitly, to say the field is handled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnprojectedClassifiedFieldAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic id.</summary>
    public const string DiagnosticId = "MOD0005";

    private const string ClassifiedAttributeTypeName = "Modulus.Core.Abstractions.Entities.ClassifiedAttribute";
    private const string ProjectionOfAttributeTypeName = "Modulus.Core.Abstractions.Entities.ProjectionOfAttribute";

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        title: "Classified field copied to a projection without [ProjectionOf]",
        messageFormat: "'{0}' has the name and type of the classified field '{1}.{0}', but '{2}' does not declare [ProjectionOf(typeof({1}))], so field security does not classify it; declare [ProjectionOf] or classify the property",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Field security applies an entity's [Classified] fields to a projection only when the projection declares [ProjectionOf]. " +
                     "A classified field copied into an undeclared DTO is returned to callers without clearance.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var classified = start.Compilation.GetTypeByMetadataName(ClassifiedAttributeTypeName);
            var projectionOf = start.Compilation.GetTypeByMetadataName(ProjectionOfAttributeTypeName);
            if (classified is null || projectionOf is null)
                return;

            var sourceTypes = new ConcurrentQueue<INamedTypeSymbol>();
            start.RegisterSymbolAction(ctx =>
            {
                var type = (INamedTypeSymbol)ctx.Symbol;
                if (type.Locations.Any(l => l.IsInSource))
                    sourceTypes.Enqueue(type);
            }, SymbolKind.NamedType);

            // Matching needs every classified field in the compilation, so the check runs once all types are known.
            start.RegisterCompilationEndAction(ctx => Analyze(ctx, sourceTypes.ToArray(), classified, projectionOf));
        });
    }

    private static void Analyze(
        CompilationAnalysisContext context,
        INamedTypeSymbol[] types,
        INamedTypeSymbol classified,
        INamedTypeSymbol projectionOf)
    {
        // Each classified field, keyed by name and type, with the type that declares it.
        var classifiedFields = new Dictionary<(string Name, string Type), List<INamedTypeSymbol>>();
        foreach (var type in types)
        {
            foreach (var property in InstanceProperties(type))
            {
                if (!HasAttribute(property, classified))
                    continue;

                var key = (property.Name, property.Type.ToDisplayString());
                if (!classifiedFields.TryGetValue(key, out var owners))
                    classifiedFields[key] = owners = [];
                owners.Add(type);
            }
        }

        if (classifiedFields.Count == 0)
            return;

        foreach (var type in types)
        {
            if (HasAttribute(type, projectionOf))
                continue;

            foreach (var property in InstanceProperties(type))
            {
                if (HasAttribute(property, classified))
                    continue;

                if (!classifiedFields.TryGetValue((property.Name, property.Type.ToDisplayString()), out var owners))
                    continue;

                var owner = owners.FirstOrDefault(o => !SymbolEqualityComparer.Default.Equals(o, type));
                if (owner is null)
                    continue;

                var location = property.Locations.FirstOrDefault(l => l.IsInSource);
                if (location is not null)
                    context.ReportDiagnostic(Diagnostic.Create(s_rule, location, property.Name, owner.Name, type.Name));
            }
        }
    }

    private static IEnumerable<IPropertySymbol> InstanceProperties(INamedTypeSymbol type)
        => type.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public);

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attribute)
        => symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
}
