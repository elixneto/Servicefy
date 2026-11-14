using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Servicefy.Package.Conventions.Generators.Emit;
using Servicefy.Package.Conventions.Models;
using Servicefy.Package.Pipeline;
using Servicefy.Package.Conventions.ByNamespace.Generators.Analysis;
using Servicefy.Package.Decorators;

namespace Servicefy.Package.Conventions.ByNamespaceOf.Generators.Emit;

/// <summary>
/// Implements <c>ServicefyConventionsBuilder.ApplyByNamespaceOf</c> for every distinct
/// <c>.ByNamespaceOf&lt;TMarker&gt;(predicate, lifetime)</c> call site found in the compilation:
/// restricts candidates to <typeparamref name="TMarker"/>'s namespace (or a sub-namespace of it),
/// matches the remaining namespaces against the predicate, then registers each directly
/// implemented interface of every matching class with the requested lifetime. When
/// <typeparamref name="TMarker"/> is declared in a NuGet package, that package's types under the
/// marker's root namespace are candidates too, and its interfaces are registrable for the rule.
/// </summary>
internal static class ByNamespaceOfRegistrationEmitter
{
    internal static void Emit(
        SourceProductionContext spc,
        (ConventionTypePool Pool, EquatableArray<NamespaceOfConventionRule> Rules) source)
    {
        var (pool, rules) = source;
        var compilation = pool.Compilation;

        var distinctRules = rules
            .DistinctBy(r => (r.MarkerTypeFqn, r.PredicateExpression, r.Lifetime))
            .ToList();

        // Rules carry the predicate as text (value-equatable pipeline model); turn each back into
        // a matching function once per emit. A predicate that no longer parses was already
        // reported (SVCFY011/012) when its call site was collected, so its rule is just skipped.
        var predicates = new Dictionary<NamespaceOfConventionRule, Func<string, bool>>();
        foreach (var rule in distinctRules)
            if (NamespacePredicateParser.ParseText(rule.PredicateExpression) is { } parsed)
                predicates[rule] = parsed;

        distinctRules = distinctRules.Where(predicates.ContainsKey).ToList();

        if (distinctRules.Count == 0) return;

        // Shared with the other convention emitters: computed once per compilation (ConventionTypePool).
        var projectReferenceAssemblies = pool.ProjectReferenceAssemblies;
        var allTypes = pool.AllTypes;
        var decoratorTypes = pool.DecoratorTypes;

        var namespaceGroups = GroupByNamespace(
            allTypes.Where(t => IsRegistrable(t, decoratorTypes, projectReferenceAssemblies)));

        // Anchor-scoped NuGet scanning (issue #8): when TMarker is declared in a NuGet package, only
        // TMarker's root-namespace subtree of that package is walked (never the whole assembly), and
        // the package's interfaces become registrable for the rule. Keyed on (package, root namespace)
        // so rules sharing a marker namespace share the work.
        var anchoredScopes = new Dictionary<(IAssemblySymbol Anchor, string Root), AnchoredScope>(AnchorKeyComparer.Instance);

        // First pass: compute matched types per rule and union all matched interfaces so
        // decorator validation runs once across the whole compilation (avoids duplicate
        // SVCFY007 diagnostics when multiple rules match overlapping interfaces).
        var ruleMatches = new List<(NamespaceOfConventionRule Rule, List<INamedTypeSymbol> MatchedTypes, HashSet<IAssemblySymbol> RegistrableAssemblies)>();
        var registeredServices = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in distinctRules)
        {
            var groups = namespaceGroups;
            var registrableAssemblies = projectReferenceAssemblies;

            var anchor = ResolveAnchor(compilation, rule);
            if (anchor is not null)
            {
                var key = (anchor, rule.RootNamespace);
                if (!anchoredScopes.TryGetValue(key, out var scope))
                    anchoredScopes[key] = scope = AnchoredScope.Create(
                        compilation, anchor, rule.RootNamespace, allTypes, decoratorTypes, projectReferenceAssemblies);

                groups = scope.NamespaceGroups;
                registrableAssemblies = scope.RegistrableAssemblies;
            }

            var matchedTypes = groups
                .Where(g => IsWithinRoot(g.Key, rule.RootNamespace) && EvaluatePredicate(predicates[rule], g.Key))
                .SelectMany(g => g)
                .OrderBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToList();

            if (matchedTypes.Count == 0) continue;

            ruleMatches.Add((rule, matchedTypes, registrableAssemblies));

            foreach (var type in matchedTypes)
            foreach (var iface in ConventionFilters.GetRegistrableInterfaces(type, registrableAssemblies))
                registeredServices.Add(iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (ruleMatches.Count == 0) return;

        var decoratorEntries = pool.GetDecoratorEntries();
        var validDecoratorMap = DecoratorEmitter.ValidateAndBuildDecoratorMap(spc, decoratorEntries, registeredServices);

        var ns = compilation.AssemblyName ?? "Servicefy";
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        sb.AppendLine("    internal sealed partial class ServicefyConventionsBuilder");
        sb.AppendLine("    {");
        sb.AppendLine("        partial void ApplyByNamespaceOf(Type markerType, Lifetime lifetime, string predicateExpression)");
        sb.AppendLine("        {");

        foreach (var (rule, matchedTypes, registrableAssemblies) in ruleMatches)
        {
            sb.AppendLine($"            if (markerType == typeof({rule.MarkerTypeFqn}) && lifetime == Lifetime.{rule.Lifetime} && predicateExpression == \"{ConventionFilters.EscapeStringLiteral(rule.PredicateExpression)}\")");
            sb.AppendLine("            {");

            foreach (var type in matchedTypes)
            {
                var implFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                foreach (var iface in ConventionFilters.GetRegistrableInterfaces(type, registrableAssemblies))
                {
                    var serviceFqn = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (validDecoratorMap.TryGetValue(serviceFqn, out var decoratorEntry))
                        DecoratorEmitter.EmitDecoratedService(sb, "                ", "Services", rule.Lifetime, serviceFqn, implFqn, decoratorEntry);
                    else
                        sb.AppendLine($"                Services.Add{rule.Lifetime}<{serviceFqn}, {implFqn}>();");
                }
            }

            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("ServicefyConventionsBuilder.ByNamespaceOf.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static List<IGrouping<string, INamedTypeSymbol>> GroupByNamespace(IEnumerable<INamedTypeSymbol> types) =>
        types
            .GroupBy(t => t.ContainingNamespace.ToDisplayString(), StringComparer.Ordinal)
            .ToList();

    // TMarker declared in a NuGet package anchors that package; a marker in the consuming project or
    // in a project reference keeps the regular scope (those types are already in the pool).
    private static IAssemblySymbol? ResolveAnchor(Compilation compilation, NamespaceOfConventionRule rule) =>
        TypeReference.Resolve(compilation, rule.MarkerTypeId)?.ContainingAssembly is { } containing
        && TypeCollector.IsNuGetPackageAssembly(compilation, containing)
            ? containing
            : null;

    /// <summary>
    /// The candidates of a rule anchored on a NuGet package: the pool's types plus the package's
    /// types under the marker's root namespace, filtered with the package's interfaces registrable.
    /// </summary>
    private sealed record AnchoredScope(
        List<IGrouping<string, INamedTypeSymbol>> NamespaceGroups,
        HashSet<IAssemblySymbol> RegistrableAssemblies)
    {
        public static AnchoredScope Create(
            Compilation compilation,
            IAssemblySymbol anchor,
            string rootNamespace,
            IReadOnlyList<INamedTypeSymbol> allTypes,
            HashSet<INamedTypeSymbol> decoratorTypes,
            HashSet<IAssemblySymbol> projectReferenceAssemblies)
        {
            var registrableAssemblies = new HashSet<IAssemblySymbol>(projectReferenceAssemblies, SymbolEqualityComparer.Default) { anchor };

            var anchorTypes = TypeCollector.GetAccessibleTypes(anchor, rootNamespace);
            var anchorDecorators = ConventionFilters.CollectDecoratorTypes(anchorTypes, compilation, []);

            // Only types under the root namespace can ever match, so the (wider) registrability check
            // runs on those alone.
            var candidates = allTypes
                .Where(t => IsWithinRoot(t.ContainingNamespace.ToDisplayString(), rootNamespace))
                .Concat(anchorTypes.Where(t => !anchorDecorators.Contains(t)))
                .Where(t => IsRegistrable(t, decoratorTypes, registrableAssemblies));

            return new AnchoredScope(GroupByNamespace(candidates), registrableAssemblies);
        }
    }

    private sealed class AnchorKeyComparer : IEqualityComparer<(IAssemblySymbol Anchor, string Root)>
    {
        public static readonly AnchorKeyComparer Instance = new();

        public bool Equals((IAssemblySymbol Anchor, string Root) x, (IAssemblySymbol Anchor, string Root) y) =>
            SymbolEqualityComparer.Default.Equals(x.Anchor, y.Anchor) && string.Equals(x.Root, y.Root, StringComparison.Ordinal);

        public int GetHashCode((IAssemblySymbol Anchor, string Root) key) =>
            (SymbolEqualityComparer.Default.GetHashCode(key.Anchor) * 397) ^ StringComparer.Ordinal.GetHashCode(key.Root);
    }

    // A namespace is "within" the marker's root namespace if it is the root namespace itself,
    // or a sub-namespace of it. A root of "" (TMarker in the global namespace) imposes no
    // restriction, since the global namespace has no meaningful sub-namespace prefix to match.
    private static bool IsWithinRoot(string @namespace, string root) =>
        root.Length == 0
        || @namespace == root
        || @namespace.StartsWith(root + ".", StringComparison.Ordinal);

    private static bool IsRegistrable(INamedTypeSymbol type, HashSet<INamedTypeSymbol> decoratorTypes, HashSet<IAssemblySymbol> projectReferenceAssemblies) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsStatic
        && type.TypeParameters.Length == 0
        // cheap checks first: interfaces/attributes of metadata types are decoded lazily
        && !ConventionFilters.InfrastructureTypeNames.Contains(type.Name)
        && !decoratorTypes.Contains(type)
        && ConventionFilters.GetRegistrableInterfaces(type, projectReferenceAssemblies).Any()
        && !ConventionFilters.HasByConfigurationAttribute(type);

    private static bool EvaluatePredicate(Func<string, bool> predicate, string @namespace)
    {
        try
        {
            return predicate(@namespace);
        }
        catch
        {
            return false;
        }
    }
}
