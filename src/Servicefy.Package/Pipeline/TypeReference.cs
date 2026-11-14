using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Pipeline;

/// <summary>
/// Value-equatable reference to a type symbol, safe to keep in incremental pipeline models.
/// Symbols can't be cached across compilations (a new compilation yields new symbol instances),
/// so models keep a documentation-comment reference id (e.g. <c>T:Ns.IRepository{Ns.Order}</c>)
/// and resolve it against the current compilation at emit time.
/// </summary>
internal static class TypeReference
{
    /// <summary>
    /// Creates a reference id for <paramref name="type"/>, or <c>null</c> when the type can't be
    /// referenced that way (e.g. error types). Unbound generics (<c>typeof(IFoo&lt;&gt;)</c>) are
    /// referenced through their definition; callers keep the "unbound" flag themselves.
    /// </summary>
    public static string? CreateId(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Error) return null;

        var target = type is INamedTypeSymbol { IsUnboundGenericType: true } unbound
            ? unbound.OriginalDefinition
            : type;

        return DocumentationCommentId.CreateReferenceId(target);
    }

    /// <summary>
    /// Resolves an id created by <see cref="CreateId"/> in <paramref name="compilation"/>.
    /// When the same name exists in several assemblies, the one declared in the compilation itself
    /// wins, then the first referenced match, mirroring how the call site would bind.
    /// </summary>
    public static INamedTypeSymbol? Resolve(Compilation compilation, string? id, bool unbound = false)
    {
        if (id is null) return null;

        var candidates = DocumentationCommentId.GetSymbolsForReferenceId(id, compilation)
            .OfType<INamedTypeSymbol>()
            .ToList();

        var resolved = candidates.FirstOrDefault(t => SymbolEqualityComparer.Default.Equals(t.ContainingAssembly, compilation.Assembly))
                       ?? candidates.FirstOrDefault();

        if (resolved is null) return null;

        return unbound && resolved.IsGenericType
            ? resolved.OriginalDefinition.ConstructUnboundGenericType()
            : resolved;
    }
}
