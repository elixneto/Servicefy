using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Generators.Analysis;

/// <summary>
/// Scans referenced assemblies for existing Servicefy-generated <c>ServicefyExtensions</c>
/// classes and resolves the chain of namespaces that each one already covers,
/// preventing double-registration in aggregator projects.
/// </summary>
internal static class AggregationScanner
{
    private static readonly HashSet<string> SystemAssemblyPrefixes =
        new(StringComparer.Ordinal) { "System", "Microsoft", "mscorlib", "netstandard", "WindowsBase" };

    internal static List<string> GetReferencedServicefyNamespaces(Compilation compilation)
    {
        var found = new List<(string Namespace, IReadOnlyList<string> Covers)>();

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;

            if (IsSystemAssembly(assembly.Name)) continue;

            var entry = FindExtensionsEntry(assembly);
            if (entry is not null)
                found.Add(entry.Value);
        }

        // Exclude namespaces already covered by a higher-level aggregator to avoid
        // double-registration when the consumer references both an aggregator and
        // one of the assemblies it aggregates.
        var coveredByOthers = new HashSet<string>(
            found.SelectMany(f => f.Covers),
            StringComparer.Ordinal);

        return found
            .Where(f => !coveredByOthers.Contains(f.Namespace))
            .Select(f => f.Namespace)
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool IsSystemAssembly(string name) =>
        name is "System" or "mscorlib" or "netstandard" or "WindowsBase" ||
        name.StartsWith("System.", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.", StringComparison.Ordinal);

    private static (string Namespace, IReadOnlyList<string> Covers)? FindExtensionsEntry(
        IAssemblySymbol assembly) =>
        SearchNamespace(assembly.GlobalNamespace);

    private static (string Namespace, IReadOnlyList<string> Covers)? SearchNamespace(
        INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers("ServicefyExtensions"))
        {
            if (type.IsStatic &&
                type.DeclaredAccessibility == Accessibility.Public &&
                HasConformingAddServicefyMethod(type))
            {
                var typeNs = type.ContainingNamespace is { IsGlobalNamespace: false } containingNs
                    ? containingNs.ToDisplayString()
                    : type.ContainingAssembly?.Name ?? string.Empty;

                return (typeNs, ReadAggregatesCoverage(type));
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            var found = SearchNamespace(child);
            if (found is not null) return found;
        }

        return null;
    }

    private static bool HasConformingAddServicefyMethod(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers("AddServicefy"))
        {
            if (member is not IMethodSymbol { IsStatic: true } method) continue;
            if (method.DeclaredAccessibility != Accessibility.Public) continue;
            if (method.Parameters.Length != 2) continue;
            if (method.Parameters[0].Type.Name != "IServiceCollection") continue;
            if (method.Parameters[1].Type.Name != "IConfiguration") continue;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ReadAggregatesCoverage(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "ServicefyAggregatesAttribute") continue;
            if (attr.ConstructorArguments.Length == 0) continue;

            var arg = attr.ConstructorArguments[0];
            if (arg.Kind == TypedConstantKind.Array)
            {
                return arg.Values
                    .Where(v => v.Value is string)
                    .Select(v => (string)v.Value!)
                    .ToList();
            }
        }

        return Array.Empty<string>();
    }
}
