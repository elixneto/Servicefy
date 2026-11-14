using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Servicefy.Package.Decorators;
using Servicefy.Package.Decorators.Analysis;

namespace Servicefy.Package.Conventions.Generators.Emit;

/// <summary>
/// Everything the convention emitters need to know about the compilation's types, computed once
/// per compilation and shared by the <c>ByNamespace</c>, <c>ByBaseType</c>, <c>ByNamespaceOf</c> and
/// <c>ByTypeName</c> emitters (each one used to walk every type, scan every syntax tree for
/// <c>.Decorate(...)</c> calls and re-collect decorators on its own).
/// </summary>
/// <remarks>
/// Built from <c>CompilationProvider</c>, so it is a new instance on every edit; the expensive parts
/// are either cached below it (referenced assemblies, see <see cref="TypeCollector.GetAccessibleTypes"/>;
/// <c>.Decorate</c> call sites, collected incrementally per file) or computed lazily, at most once,
/// by whichever emitter needs them first.
/// </remarks>
internal sealed class ConventionTypePool
{
    private readonly ImmutableArray<DecorateCall> _decorateCalls;
    private readonly Lazy<HashSet<INamedTypeSymbol>> _decoratorTypes;
    private readonly Lazy<List<InterfaceDecoratorEntry>> _decoratorEntries;
    private readonly Lazy<List<(INamedTypeSymbol UnboundService, List<INamedTypeSymbol> UnboundDecorators, Location Location)>> _openGenericChains;
    private readonly Lazy<List<IAssemblySymbol>> _packageAssemblies;
    private readonly Lazy<HashSet<IAssemblySymbol>> _allReferencesRegistrableAssemblies;

    public ConventionTypePool(Compilation compilation, ImmutableArray<DecorateCall> decorateCalls)
    {
        Compilation = compilation;
        _decorateCalls = decorateCalls;

        ProjectReferenceAssemblies = TypeCollector.GetProjectReferenceAssemblies(compilation)
            .ToHashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        AllTypes = TypeCollector.GetAllAccessibleTypes(compilation)
            .Concat(TypeCollector.GetExternalAccessibleTypes(compilation))
            .Select(t => t.Type)
            .ToList();

        _decoratorTypes = new Lazy<HashSet<INamedTypeSymbol>>(
            () => ConventionFilters.CollectDecoratorTypes(AllTypes, Compilation, _decorateCalls));
        _decoratorEntries = new Lazy<List<InterfaceDecoratorEntry>>(
            () => DecoratorCollector.CollectDecoratorEntries(Compilation, AllTypes, _decorateCalls));
        _openGenericChains = new Lazy<List<(INamedTypeSymbol, List<INamedTypeSymbol>, Location)>>(
            () => DecoratorCollector.CollectOpenGenericChains(Compilation, _decorateCalls));
        _packageAssemblies = new Lazy<List<IAssemblySymbol>>(
            () => TypeCollector.GetPackageReferenceAssemblies(Compilation).ToList());
        _allReferencesRegistrableAssemblies = new Lazy<HashSet<IAssemblySymbol>>(
            () => new HashSet<IAssemblySymbol>(ProjectReferenceAssemblies.Concat(_packageAssemblies.Value), SymbolEqualityComparer.Default));
    }

    public Compilation Compilation { get; }

    /// <summary>Referenced assemblies treated as <c>&lt;ProjectReference&gt;</c>s (see <see cref="TypeCollector"/>).</summary>
    public HashSet<IAssemblySymbol> ProjectReferenceAssemblies { get; }

    /// <summary>Types declared in the compilation plus public types of project-reference assemblies.</summary>
    public IReadOnlyList<INamedTypeSymbol> AllTypes { get; }

    /// <summary>Classes acting as decorators, which conventions must never register as plain services.</summary>
    public HashSet<INamedTypeSymbol> DecoratorTypes => _decoratorTypes.Value;

    /// <summary>
    /// A fresh copy of the closed decorator entries; callers may add synthesized entries to it
    /// (e.g. closed forms of open-generic chains) without affecting other emitters.
    /// </summary>
    public List<InterfaceDecoratorEntry> GetDecoratorEntries() => [.. _decoratorEntries.Value];

    /// <summary>Open-generic <c>.Decorate(typeof(IFoo&lt;&gt;), typeof(D&lt;&gt;))</c> chains per unbound service.</summary>
    public IReadOnlyList<(INamedTypeSymbol UnboundService, List<INamedTypeSymbol> UnboundDecorators, Location Location)> OpenGenericChains
        => _openGenericChains.Value;

    /// <summary>
    /// Assemblies whose interfaces are registrable under <c>ConventionScope.AllReferences</c>:
    /// project references plus every referenced NuGet package (see <see cref="TypeCollector.GetPackageReferenceAssemblies"/>).
    /// </summary>
    public HashSet<IAssemblySymbol> AllReferencesRegistrableAssemblies => _allReferencesRegistrableAssemblies.Value;

    /// <summary>
    /// Candidates under <c>ConventionScope.AllReferences</c>: <see cref="AllTypes"/> plus the public types of
    /// every referenced NuGet package. With <paramref name="namespacePrefixes"/>, packages are only walked
    /// under the namespaces that can start with one of them (see <see cref="TypeCollector"/>); each walk is
    /// cached per assembly/namespace symbol, so it happens once per reference set, not on every edit.
    /// </summary>
    public IEnumerable<INamedTypeSymbol> GetAllReferencesTypes(IReadOnlyList<string>? namespacePrefixes, StringComparison comparison) =>
        AllTypes.Concat(_packageAssemblies.Value.SelectMany(assembly => namespacePrefixes is null
            ? TypeCollector.GetAccessibleTypes(assembly)
            : TypeCollector.GetAccessibleTypes(assembly, namespacePrefixes, comparison)));

    /// <summary>Whether <paramref name="type"/> comes from a NuGet package (not from source or a project reference).</summary>
    public bool IsPackageType(INamedTypeSymbol type) =>
        !type.Locations.Any(l => l.IsInSource) && !ProjectReferenceAssemblies.Contains(type.ContainingAssembly);
}
