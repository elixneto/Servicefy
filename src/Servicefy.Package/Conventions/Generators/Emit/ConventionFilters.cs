using Microsoft.CodeAnalysis;
using Servicefy.Package.ByConfiguration.Generators.Analysis;

namespace Servicefy.Package.Conventions.Generators.Emit;

/// <summary>
/// Filters shared by all convention-based registration emitters (<c>ByNamespace</c>,
/// <c>ByBaseType</c>, ...): excludes generated infrastructure types, types already
/// opted into <c>ByConfiguration</c>, and types used as <c>[DecoratorFor&lt;T&gt;]</c> / <c>.Decorate&lt;,&gt;()</c> targets.
/// </summary>
internal static class ConventionFilters
{
    // Generated infrastructure types live in the same namespace/assembly as user code and
    // would otherwise be picked up by a broad enough convention.
    internal static readonly HashSet<string> InfrastructureTypeNames = new(StringComparer.Ordinal)
    {
        "ServicefyConventionsBuilder",
    };

    // Classes already opted into ByConfiguration (e.g. via [AddScoped]/[AddKeyedScoped]/[Configure])
    // must not also be picked up by convention-based registration, which would duplicate
    // an existing registration.
    internal static bool HasByConfigurationAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (AttributeParser.TryGetServiceRegistration(attribute, out _, out _)) return true;
            if (AttributeParser.TryGetKeyedServiceRegistration(attribute, out _, out _, out _)) return true;
            if (AttributeParser.TryGetConfigureRegistration(attribute, out _, out _)) return true;
        }

        return false;
    }

    // Classes used as [DecoratorFor<T>] targets or as the TDecorator of a .Decorate<,>() call
    // typically carry no registration attribute at all (SVCFY008 forbids non-keyed [Add*] on them)
    // but must still be excluded from convention-based registration — otherwise they'd be
    // registered as plain services, breaking the decorator chain.
    internal static HashSet<INamedTypeSymbol> CollectDecoratorTypes(List<INamedTypeSymbol> types, Compilation compilation)
    {
        var decoratorTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var type in types)
        {
            if (type.TypeKind != TypeKind.Class) continue;

            foreach (var attribute in type.GetAttributes())
            {
                if (AttributeParser.TryGetDecoratorForAttribute(attribute, out var serviceType) && serviceType is not null)
                    decoratorTypes.Add(type);

                if (AttributeParser.TryGetDecoratorAttribute(attribute) && type.Interfaces.Length == 1)
                    decoratorTypes.Add(type);
            }
        }

        foreach (var (_, decorator, _) in Decorators.Analysis.DecorateCallCollector.Collect(compilation))
            decoratorTypes.Add(decorator);

        return decoratorTypes;
    }

    // Convention-based registration only considers interfaces declared in the user's own source,
    // or in a referenced project (e.g. via [DecoratorFor<T>] targets or directly implemented interfaces).
    // Framework/BCL interfaces like System.IDisposable have no source location and aren't declared
    // in a project reference either, and must never be auto-registered as services — checking the
    // namespace would be unreliable (a user-defined interface can live under "Microsoft.*" or
    // "System.*" too), so this checks where the interface is actually declared.
    internal static IEnumerable<INamedTypeSymbol> GetRegistrableInterfaces(
        INamedTypeSymbol type, HashSet<IAssemblySymbol> projectReferenceAssemblies) =>
        type.Interfaces.Where(i => IsUserDefinedInterface(i, projectReferenceAssemblies));

    internal static bool IsUserDefinedInterface(INamedTypeSymbol iface, HashSet<IAssemblySymbol> projectReferenceAssemblies) =>
        iface.Locations.Any(l => l.IsInSource) || projectReferenceAssemblies.Contains(iface.ContainingAssembly);

    internal static string EscapeStringLiteral(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
}
