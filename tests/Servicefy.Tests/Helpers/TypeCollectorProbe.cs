using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Servicefy.Package;

namespace Servicefy.Tests.Helpers;

internal static class TypeCollectorProbe
{
    private static readonly ConditionalWeakTable<IAssemblySymbol, INamedTypeSymbol[]> ByAssembly =
        GetCache<IAssemblySymbol>("AccessibleTypesByAssembly");

    private static readonly ConditionalWeakTable<INamespaceSymbol, INamedTypeSymbol[]> ByNamespace =
        GetCache<INamespaceSymbol>("AccessibleTypesByNamespace");

    public static bool IsAssemblyWalked(IAssemblySymbol assembly) => ByAssembly.TryGetValue(assembly, out _);

    public static INamedTypeSymbol[]? AssemblyWalk(IAssemblySymbol assembly) =>
        ByAssembly.TryGetValue(assembly, out var types) ? types : null;

    public static INamedTypeSymbol[]? NamespaceWalk(INamespaceSymbol ns) =>
        ByNamespace.TryGetValue(ns, out var types) ? types : null;

    public static IReadOnlyList<string> WalkedNamespaces(IAssemblySymbol assembly) =>
        AllNamespaces(assembly.GlobalNamespace)
            .Where(ns => ByNamespace.TryGetValue(ns, out _))
            .Select(ns => ns.ToDisplayString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public static bool IsNeverWalked(IAssemblySymbol assembly) =>
        !IsAssemblyWalked(assembly) && WalkedNamespaces(assembly).Count == 0;

    public static INamespaceSymbol Namespace(IAssemblySymbol assembly, string fullName) =>
        AllNamespaces(assembly.GlobalNamespace).Single(ns => ns.ToDisplayString() == fullName);

    public static IAssemblySymbol ReferencedAssembly(Compilation compilation, string name) =>
        compilation.SourceModule.ReferencedAssemblySymbols.Single(a => a.Name == name);

    private static IEnumerable<INamespaceSymbol> AllNamespaces(INamespaceSymbol parent)
    {
        foreach (var child in parent.GetNamespaceMembers())
        {
            yield return child;
            foreach (var nested in AllNamespaces(child))
                yield return nested;
        }
    }

    private static ConditionalWeakTable<TKey, INamedTypeSymbol[]> GetCache<TKey>(string fieldName) where TKey : class
    {
        var field = typeof(TypeCollector).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"TypeCollector.{fieldName} not found: the walk cache this probe reads has moved.");

        return field.GetValue(null) as ConditionalWeakTable<TKey, INamedTypeSymbol[]>
            ?? throw new InvalidOperationException($"TypeCollector.{fieldName} is not a ConditionalWeakTable<{typeof(TKey).Name}, INamedTypeSymbol[]>.");
    }
}
