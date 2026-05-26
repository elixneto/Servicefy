using Microsoft.CodeAnalysis;
using Servicefy.Package.Conventions.ByBaseType.Generators.Analysis;
using Servicefy.Package.Conventions.ByBaseType.Generators.Emit;
using Servicefy.Package.Conventions.ByNamespace.Generators.Analysis;
using Servicefy.Package.Conventions.ByNamespace.Generators.Emit;
using Servicefy.Package.Conventions.ByNamespaceOf.Generators.Analysis;
using Servicefy.Package.Conventions.ByNamespaceOf.Generators.Emit;
using Servicefy.Package.Conventions.ByTypeName.Generators.Analysis;
using Servicefy.Package.Conventions.ByTypeName.Generators.Emit;

namespace Servicefy.Package.Conventions.Generators;

/// <summary>
/// Entry point for the Servicefy ByConvention incremental source generator.
/// Wires up the <c>ByNamespace</c>, <c>ByBaseType</c>, <c>ByNamespaceOf</c> and
/// <c>ByTypeName</c> conventions: emits the <c>ServicefyConventionsBuilder</c> runtime API, then
/// detects <c>.ByNamespace(predicate, lifetime)</c> / <c>.ByBaseType&lt;TBase&gt;(...)</c> /
/// <c>.ByNamespaceOf&lt;TMarker&gt;(predicate, lifetime)</c> / <c>.ByTypeName(predicate, lifetime)</c>
/// call sites and implements the corresponding registrations.
/// </summary>
[Generator]
public sealed class ServicefyConventionsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Emit the AddServicefyConventions() / ByNamespace / ByBaseType runtime API
        context.RegisterSourceOutput(
            context.CompilationProvider,
            ConventionsBuilderEmitter.Emit);

        // 2. Collect .ByNamespace(predicate, lifetime) call sites
        var byNamespaceResults = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => ByNamespaceCallCollector.IsCandidate(node),
                transform: static (ctx, _) => ByNamespaceCallCollector.Transform(ctx));

        context.RegisterSourceOutput(byNamespaceResults.Collect(), static (spc, results) =>
        {
            foreach (var (_, diagnostic) in results)
                if (diagnostic is not null)
                    spc.ReportDiagnostic(diagnostic);
        });

        var byNamespaceRules = byNamespaceResults
            .Where(static r => r.Rule is not null)
            .Select(static (r, _) => r.Rule!);

        // 3. Implement ServicefyConventionsBuilder.ApplyByNamespace for each rule
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(byNamespaceRules.Collect()),
            ByNamespaceRegistrationEmitter.Emit);

        // 4. Collect .ByBaseType<TBase>(lifetime, selector, attribute) call sites
        var byBaseTypeRules = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => ByBaseTypeCallCollector.IsCandidate(node),
                transform: static (ctx, _) => ByBaseTypeCallCollector.Transform(ctx))
            .Where(static rule => rule is not null)
            .Select(static (rule, _) => rule!);

        // 5. Implement ServicefyConventionsBuilder.ApplyByBaseType for each rule
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(byBaseTypeRules.Collect()),
            ByBaseTypeRegistrationEmitter.Emit);

        // 6. Collect .ByNamespaceOf<TMarker>(predicate, lifetime) call sites
        var byNamespaceOfResults = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => ByNamespaceOfCallCollector.IsCandidate(node),
                transform: static (ctx, _) => ByNamespaceOfCallCollector.Transform(ctx));

        context.RegisterSourceOutput(byNamespaceOfResults.Collect(), static (spc, results) =>
        {
            foreach (var (_, diagnostic) in results)
                if (diagnostic is not null)
                    spc.ReportDiagnostic(diagnostic);
        });

        var byNamespaceOfRules = byNamespaceOfResults
            .Where(static r => r.Rule is not null)
            .Select(static (r, _) => r.Rule!);

        // 7. Implement ServicefyConventionsBuilder.ApplyByNamespaceOf for each rule
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(byNamespaceOfRules.Collect()),
            ByNamespaceOfRegistrationEmitter.Emit);

        // 8. Collect .ByTypeName(predicate, lifetime) call sites
        var byTypeNameResults = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => ByTypeNameCallCollector.IsCandidate(node),
                transform: static (ctx, _) => ByTypeNameCallCollector.Transform(ctx));

        context.RegisterSourceOutput(byTypeNameResults.Collect(), static (spc, results) =>
        {
            foreach (var (_, diagnostic) in results)
                if (diagnostic is not null)
                    spc.ReportDiagnostic(diagnostic);
        });

        var byTypeNameRules = byTypeNameResults
            .Where(static r => r.Rule is not null)
            .Select(static (r, _) => r.Rule!);

        // 9. Implement ServicefyConventionsBuilder.ApplyByTypeName for each rule
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(byTypeNameRules.Collect()),
            ByTypeNameRegistrationEmitter.Emit);
    }
}
