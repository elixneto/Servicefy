using System.Runtime.CompilerServices;
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
        foreach (var type in GetAccessibleTypes(assembly))
            yield return (type, assembly.Name);
    }

    /// <summary>
    /// The publicly visible types of a referenced (metadata) assembly, walked once per assembly symbol.
    /// </summary>
    /// <remarks>
    /// Roslyn reuses the same <see cref="IAssemblySymbol"/> for an unchanged reference across the
    /// compilations produced by successive edits, so keying a weak cache on it means a referenced
    /// assembly is walked once per reference set instead of on every keystroke. The table holds the
    /// symbol weakly, so entries go away together with the compilations that use them.
    /// </remarks>
    internal static IReadOnlyList<INamedTypeSymbol> GetAccessibleTypes(IAssemblySymbol assembly) =>
        AccessibleTypesByAssembly.GetValue(
            assembly,
            static a => WalkNamespace(a.GlobalNamespace).Where(IsExternallyAccessible).ToArray());

    private static readonly ConditionalWeakTable<IAssemblySymbol, INamedTypeSymbol[]> AccessibleTypesByAssembly = new();

    /// <summary>
    /// The publicly visible types of <paramref name="assembly"/> declared in
    /// <paramref name="rootNamespace"/> or one of its sub-namespaces — the rest of the assembly is
    /// never walked. An empty <paramref name="rootNamespace"/> (the global namespace) means the whole
    /// assembly. Cached per namespace symbol, for the same reason as <see cref="GetAccessibleTypes(IAssemblySymbol)"/>.
    /// </summary>
    internal static IReadOnlyList<INamedTypeSymbol> GetAccessibleTypes(IAssemblySymbol assembly, string rootNamespace)
    {
        if (rootNamespace.Length == 0) return GetAccessibleTypes(assembly);

        INamespaceSymbol? ns = assembly.GlobalNamespace;
        foreach (var segment in rootNamespace.Split('.'))
        {
            ns = ns.GetNamespaceMembers().FirstOrDefault(n => n.Name == segment);
            if (ns is null) return [];
        }

        return GetAccessibleTypes(ns);
    }

    /// <summary>
    /// The publicly visible types declared in <paramref name="ns"/> (a namespace of a referenced
    /// assembly) or one of its sub-namespaces, walked once per namespace symbol.
    /// </summary>
    internal static IReadOnlyList<INamedTypeSymbol> GetAccessibleTypes(INamespaceSymbol ns) =>
        ns.IsGlobalNamespace
            ? GetAccessibleTypes(ns.ContainingAssembly)
            : AccessibleTypesByNamespace.GetValue(ns, static n => WalkNamespace(n).Where(IsExternallyAccessible).ToArray());

    /// <summary>
    /// The publicly visible types of <paramref name="assembly"/> whose namespace starts with one of
    /// <paramref name="namespacePrefixes"/> (plain string prefixes, as <c>string.StartsWith</c>). Only
    /// the namespaces that can contain such types are walked: <c>"MyCompany.Data"</c> descends
    /// <c>MyCompany</c> and then walks <c>MyCompany.Data</c> and <c>MyCompany.DataAccess</c>, nothing else.
    /// </summary>
    internal static IEnumerable<INamedTypeSymbol> GetAccessibleTypes(
        IAssemblySymbol assembly, IReadOnlyList<string> namespacePrefixes, StringComparison comparison)
    {
        if (namespacePrefixes.Any(p => p.Length == 0))
            return GetAccessibleTypes(assembly);

        return FindPrefixRoots(assembly.GlobalNamespace, "", namespacePrefixes, comparison)
            .SelectMany(GetAccessibleTypes);
    }

    // The top-most namespaces whose full name starts with a prefix; their whole subtree matches.
    private static IEnumerable<INamespaceSymbol> FindPrefixRoots(
        INamespaceSymbol parent, string parentName, IReadOnlyList<string> prefixes, StringComparison comparison)
    {
        foreach (var child in parent.GetNamespaceMembers())
        {
            var name = parentName.Length == 0 ? child.Name : parentName + "." + child.Name;

            if (prefixes.Any(p => name.StartsWith(p, comparison)))
            {
                yield return child;
                continue;
            }

            if (!prefixes.Any(p => p.StartsWith(name + ".", comparison))) continue;

            foreach (var root in FindPrefixRoots(child, name, prefixes, comparison))
                yield return root;
        }
    }

    private static readonly ConditionalWeakTable<INamespaceSymbol, INamedTypeSymbol[]> AccessibleTypesByNamespace = new();

    internal static IEnumerable<IAssemblySymbol> GetProjectReferenceAssemblies(Compilation compilation) =>
        compilation.SourceModule.ReferencedAssemblySymbols
            .Where(assembly => IsProjectReference(compilation, assembly));

    /// <summary>
    /// Every referenced NuGet package assembly, transitive dependencies included (MSBuild passes the
    /// whole package closure as direct references). Targeting packs restored into the packages folder
    /// count as SDK, not as packages. Scanned only under <c>ConventionScope.AllReferences</c>.
    /// </summary>
    internal static IEnumerable<IAssemblySymbol> GetPackageReferenceAssemblies(Compilation compilation) =>
        compilation.SourceModule.ReferencedAssemblySymbols
            .Where(assembly => Classify(compilation, assembly) == ReferenceKind.Package);

    /// <summary>
    /// Whether <paramref name="assembly"/> was resolved from the NuGet global packages folder, i.e.
    /// comes from a <c>&lt;PackageReference&gt;</c>. Such assemblies are never scanned wholesale; a
    /// convention may opt one in by anchoring on a type it declares (see <c>ByBaseType&lt;TBase&gt;</c> and
    /// <c>ByNamespaceOf&lt;TMarker&gt;</c>).
    /// </summary>
    internal static bool IsNuGetPackageAssembly(Compilation compilation, IAssemblySymbol assembly) =>
        Classify(compilation, assembly) == ReferenceKind.Package;

    // There is no compilation-level flag for "this reference came from a <ProjectReference>" (or a
    // <PackageReference>) — those are MSBuild concepts. We approximate them from the resolved assembly
    // path: NuGet packages restore under the global packages folder, and the SDK's own
    // reference/runtime assemblies live under dotnet's packs/shared folders.
    private static bool IsProjectReference(Compilation compilation, IAssemblySymbol assembly) =>
        Classify(compilation, assembly) == ReferenceKind.Project;

    private enum ReferenceKind { Other, Sdk, Package, Project }

    private static ReferenceKind Classify(Compilation compilation, IAssemblySymbol assembly)
    {
        if (compilation.GetMetadataReference(assembly) is not PortableExecutableReference { FilePath: { } path })
            return ReferenceKind.Other;

        var normalized = path.Replace('\\', '/');

        if (normalized.Contains("/dotnet/packs/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/dotnet/shared/", StringComparison.OrdinalIgnoreCase))
            return ReferenceKind.Sdk;

        var packagesFolder = normalized.IndexOf("/.nuget/packages/", StringComparison.OrdinalIgnoreCase);
        if (packagesFolder >= 0)
            return IsTargetingPackId(normalized.Substring(packagesFolder + "/.nuget/packages/".Length).Split('/')[0])
                ? ReferenceKind.Sdk
                : ReferenceKind.Package;

        // Anything else (typically a sibling project's build output under its own bin/ folder) is
        // treated as a project reference.
        return ReferenceKind.Project;
    }

    // Reference/targeting packs are downloaded into the packages folder when the SDK does not ship
    // them (e.g. microsoft.netcore.app.ref for another TFM, netstandard.library for netstandard2.0):
    // they are the framework, not a package.
    private static bool IsTargetingPackId(string packageId) =>
        packageId.EndsWith(".app.ref", StringComparison.OrdinalIgnoreCase)
        || packageId.StartsWith("netstandard.library", StringComparison.OrdinalIgnoreCase)
        || packageId.StartsWith("microsoft.netframework.referenceassemblies", StringComparison.OrdinalIgnoreCase);

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
