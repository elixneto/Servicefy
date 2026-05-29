using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Generators.Analysis;

/// <summary>
/// Enumerates all named types accessible within a compilation, including nested types.
/// Each result is paired with the assembly key it belongs to.
/// </summary>
internal static class TypeCollector
{
    internal static IEnumerable<(INamedTypeSymbol Type, string AssemblyKey)> GetAllAccessibleTypes(
        Compilation compilation)
    {
        var assemblyKey = compilation.AssemblyName ?? "Servicefy";
        foreach (var type in WalkNamespace(compilation.Assembly.GlobalNamespace))
            yield return (type, assemblyKey);
    }

    private static IEnumerable<INamedTypeSymbol> WalkNamespace(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in WalkNestedTypes(type))
                yield return nested;
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        foreach (var type in WalkNamespace(childNs))
            yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> WalkNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deep in WalkNestedTypes(nested))
                yield return deep;
        }
    }
}
