using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Modulus.Analyzers;

/// <summary>
/// MOD0006: a message type that is sent, published or scheduled from source has no <c>[MessageUrn]</c>. Its wire identity
/// is then derived from its CLR name, so a rename changes what consumers receive (RULE-10). Only reported when the project
/// sets <c>ModulusRequireMessageUrn</c> to <c>true</c> (and makes it visible to analyzers with
/// <c>&lt;CompilerVisibleProperty Include="ModulusRequireMessageUrn" /&gt;</c>), so existing projects are not affected. Turn it
/// on for the services whose messages are released.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageUrnAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic id.</summary>
    public const string DiagnosticId = "MOD0006";

    /// <summary>The MSBuild property that turns the rule on.</summary>
    public const string RequireProperty = "build_property.ModulusRequireMessageUrn";

    private const string UrnAttributeTypeName = "Modulus.Messaging.MessageUrnAttribute";
    private static readonly string[] BusTypeNames = ["IBus", "IPublishEndpoint", "ISendEndpoint", "IMessageScheduler"];
    private static readonly string[] SendMethodNames = ["Send", "Publish", "ScheduleSend", "SchedulePublish", "SendRequest"];

    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticId,
        title: "Released message type has no MessageUrn",
        messageFormat: "'{0}' is sent or published but has no [MessageUrn]; its wire identity is derived from its CLR name and changes when it is renamed",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Put [MessageUrn(\"urn:...\")] on every message type that is released (RULE-10). Enabled per project with " +
                     "ModulusRequireMessageUrn=true.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var enabled = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(RequireProperty, out var value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            if (!enabled)
                return;

            var urnAttribute = start.Compilation.GetTypeByMetadataName(UrnAttributeTypeName);
            if (urnAttribute is null)
                return;

            start.RegisterOperationAction(ctx => AnalyzeInvocation(ctx, urnAttribute), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol urnAttribute)
    {
        if (context.Operation is not IInvocationOperation invocation)
            return;

        var method = invocation.TargetMethod;
        if (!method.IsGenericMethod || method.TypeArguments.Length != 1 || !SendMethodNames.Contains(method.Name))
            return;

        var container = method.ContainingType?.OriginalDefinition;
        if (container is null || container.ContainingNamespace.ToDisplayString() != "Modulus.Messaging"
            || !BusTypeNames.Contains(container.Name))
            return;

        if (method.TypeArguments[0] is not INamedTypeSymbol message || message.IsAbstract && message.TypeKind == TypeKind.Interface)
            return;

        // Only messages declared in this project: a framework or library type is the owner's to annotate.
        if (message.Locations.All(location => !location.IsInSource))
            return;

        if (message.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, urnAttribute)))
            return;

        context.ReportDiagnostic(Diagnostic.Create(s_rule, invocation.Syntax.GetLocation(), message.Name));
    }
}
