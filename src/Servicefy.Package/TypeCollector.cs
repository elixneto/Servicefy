using Microsoft.CodeAnalysis;

namespace Servicefy.Package;

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

    /// <summary>
    /// Enumerates publicly visible types declared in referenced <c>ProjectReference</c> assemblies
    /// (e.g. a sibling "Features" project), so convention-based registration can pick up types that
    /// don't live in the assembly being compiled. NuGet packages and SDK reference assemblies are
    /// excluded — see <see cref="IsProjectReference"/>.
    /// </summary>
    internal static IEnumerable<(INamedTypeSymbol Type, string AssemblyKey)> GetExternalAccessibleTypes(
        Compilation compilation)
    {
        foreach (var assembly in GetProjectReferenceAssemblies(compilation))
        foreach (var type in WalkNamespace(assembly.GlobalNamespace))
        {
            if (IsExternallyAccessible(type))
                yield return (type, assembly.Name);
        }
    }

    internal static IEnumerable<IAssemblySymbol> GetProjectReferenceAssemblies(Compilation compilation) =>
        compilation.SourceModule.ReferencedAssemblySymbols
            .Where(assembly => IsProjectReference(compilation, assembly));

    // There is no compilation-level flag for "this reference came from a <ProjectReference>" — it's
    // an MSBuild concept. We approximate it from the resolved assembly path: NuGet packages restore
    // under the global packages folder, and the SDK's own reference/runtime assemblies live under
    // dotnet's packs/shared folders. Anything else (typically a sibling project's build output under
    // its own bin/ folder) is treated as a project reference.
    private static bool IsProjectReference(Compilation compilation, IAssemblySymbol assembly)
    {
        if (compilation.GetMetadataReference(assembly) is not PortableExecutableReference { FilePath: { } path })
            return false;

        var normalized = path.Replace('\\', '/');

        if (normalized.Contains("/.nuget/packages/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (normalized.Contains("/dotnet/packs/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/dotnet/shared/", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    // Only types reachable from the consuming assembly can be passed to Services.AddXxx<...>(),
    // so every type in the containment chain (including the type itself) must be public.
    private static bool IsExternallyAccessible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public) return false;
        }

        return true;
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
