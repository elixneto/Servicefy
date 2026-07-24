using Microsoft.CodeAnalysis;
using Servicefy.Package.ByConfiguration.Generators.Analysis;
using Servicefy.Package.Decorators.Analysis;

namespace Servicefy.Package.Decorators;

/// <summary>
/// Scans the compilation for <c>[DecoratorFor&lt;TService&gt;]</c>-attributed classes and
/// <c>.Decorate&lt;TService, TDecorator&gt;()</c> calls, producing one
/// <see cref="InterfaceDecoratorEntry"/> per decorated interface with a merged decorator chain.
/// Shared between ByConfiguration and ByConvention generators.
/// </summary>
internal static class DecoratorCollector
{
    /// <summary>
    /// Collects <c>.Decorate(typeof(IFoo&lt;&gt;), typeof(Decorator&lt;&gt;))</c> calls into one merged,
    /// declaration-ordered chain per unbound service definition. These are expanded into closed
    /// <see cref="InterfaceDecoratorEntry"/> instances by the registration emitter, once the concrete
    /// closed services a convention registers are known.
    /// </summary>
    internal static List<(INamedTypeSymbol UnboundService, List<INamedTypeSymbol> UnboundDecorators, Location Location)> CollectOpenGenericChains(
        Compilation compilation)
    {
        var byService = new Dictionary<INamedTypeSymbol, List<(INamedTypeSymbol Decorator, Location Location)>>(
            SymbolEqualityComparer.Default);

        foreach (var (service, decorator, location) in DecorateCallCollector.CollectOpenGeneric(compilation))
        {
            if (!byService.TryGetValue(service, out var list))
                byService[service] = list = [];

            list.Add((decorator, location));
        }

        var chains = new List<(INamedTypeSymbol, List<INamedTypeSymbol>, Location)>();
        foreach (var (service, list) in byService)
        {
            // Same fluent-chain ordering as the closed collector: sort by source span end so the
            // layers come out in left-to-right declaration order.
            list.Sort((a, b) =>
            {
                var pathCompare = string.CompareOrdinal(a.Location.SourceTree?.FilePath, b.Location.SourceTree?.FilePath);
                return pathCompare != 0 ? pathCompare : a.Location.SourceSpan.End.CompareTo(b.Location.SourceSpan.End);
            });

            var decorators = new List<INamedTypeSymbol>();
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var (decorator, _) in list)
                if (seen.Add(decorator))
                    decorators.Add(decorator);

            chains.Add((service, decorators, list[0].Location));
        }

        return chains;
    }

    internal static List<InterfaceDecoratorEntry> CollectDecoratorEntries(Compilation compilation)
    {
        var allTypes = TypeCollector.GetAllAccessibleTypes(compilation)
            .Concat(TypeCollector.GetExternalAccessibleTypes(compilation));

        // Pass 1: [DecoratorFor<TService>] attributes on classes — inner/base layer, sorted by FQN.
        var attributeDecorators = new Dictionary<INamedTypeSymbol, List<(INamedTypeSymbol Decorator, Location? Location)>>(
            SymbolEqualityComparer.Default);

        foreach (var (type, _) in allTypes)
        {
            if (type.TypeKind != TypeKind.Class) continue;

            foreach (var attr in type.GetAttributes())
            {
                INamedTypeSymbol? serviceType;

                if (AttributeParser.TryGetDecoratorForAttribute(attr, out serviceType))
                {
                    if (serviceType is null || serviceType.TypeKind != TypeKind.Interface) continue;
                }
                else if (AttributeParser.TryGetDecoratorAttribute(attr))
                {
                    // [Decorator] infers TService from the single interface the class implements;
                    // classes with zero or multiple interfaces are reported as SVCFY014 and ignored here.
                    if (type.Interfaces.Length != 1) continue;
                    serviceType = type.Interfaces[0];
                }
                else
                {
                    continue;
                }

                if (!attributeDecorators.TryGetValue(serviceType, out var list))
                    attributeDecorators[serviceType] = list = [];

                var location = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation();
                list.Add((type, location));
            }
        }

        foreach (var list in attributeDecorators.Values)
        {
            list.Sort((a, b) => string.CompareOrdinal(
                a.Decorator.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                b.Decorator.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        // Pass 2: .Decorate<TService, TDecorator>() calls — outer layers, in declaration order.
        var fluentDecorators = new Dictionary<INamedTypeSymbol, List<(INamedTypeSymbol Decorator, Location Location)>>(
            SymbolEqualityComparer.Default);

        foreach (var (service, decorator, location) in DecorateCallCollector.Collect(compilation))
        {
            if (service.TypeKind != TypeKind.Interface) continue;

            if (!fluentDecorators.TryGetValue(service, out var list))
                fluentDecorators[service] = list = [];

            list.Add((decorator, location));
        }

        foreach (var list in fluentDecorators.Values)
        {
            // For a fluent chain `x.Decorate<,A>().Decorate<,B>()`, both invocation nodes share the
            // same Span.Start (they both start at `x`) — only Span.End differs, with the outermost
            // call (B, visited first by DescendantNodes' pre-order traversal) having the larger End.
            // Sorting by End ascending yields left-to-right declaration order: [A, B].
            list.Sort((a, b) =>
            {
                var pathCompare = string.CompareOrdinal(a.Location.SourceTree?.FilePath, b.Location.SourceTree?.FilePath);
                return pathCompare != 0 ? pathCompare : a.Location.SourceSpan.End.CompareTo(b.Location.SourceSpan.End);
            });
        }

        // Merge: fluent (declaration order) ++ attribute (FQN order).
        var serviceTypes = new List<INamedTypeSymbol>(fluentDecorators.Keys);
        foreach (var serviceType in attributeDecorators.Keys)
        {
            if (!fluentDecorators.ContainsKey(serviceType))
                serviceTypes.Add(serviceType);
        }

        var entries = new List<InterfaceDecoratorEntry>();

        foreach (var serviceType in serviceTypes)
        {
            var decoratorFqns    = new List<string>();
            var decoratorSymbols = new List<INamedTypeSymbol>();
            var duplicateDecorators = new List<(string DecoratorFqn, Location? Location)>();
            var seenDecorators = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            Location? diagnosticLocation = null;

            if (fluentDecorators.TryGetValue(serviceType, out var fluentList))
            {
                foreach (var (decorator, location) in fluentList)
                {
                    diagnosticLocation ??= location;

                    if (!seenDecorators.Add(decorator))
                    {
                        duplicateDecorators.Add((decorator.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), location));
                        continue;
                    }

                    decoratorFqns.Add(decorator.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    decoratorSymbols.Add(decorator);
                }
            }

            if (attributeDecorators.TryGetValue(serviceType, out var attrList))
            {
                foreach (var (decorator, location) in attrList)
                {
                    diagnosticLocation ??= location;

                    if (!seenDecorators.Add(decorator))
                    {
                        duplicateDecorators.Add((decorator.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), location));
                        continue;
                    }

                    decoratorFqns.Add(decorator.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    decoratorSymbols.Add(decorator);
                }
            }

            if (decoratorFqns.Count == 0) continue;

            entries.Add(CreateEntry(
                compilation, serviceType, decoratorFqns, decoratorSymbols, diagnosticLocation, duplicateDecorators));
        }

        return entries;
    }

    /// <summary>
    /// Builds an <see cref="InterfaceDecoratorEntry"/> for <paramref name="serviceType"/> with the
    /// given (already ordered) decorator chain, computing the assembly key / namespace the same way
    /// as the closed-form collector. Used both internally and when synthesizing closed entries from
    /// open-generic <c>.Decorate(typeof(IFoo&lt;&gt;), typeof(Decorator&lt;&gt;))</c> chains.
    /// </summary>
    internal static InterfaceDecoratorEntry CreateEntry(
        Compilation compilation,
        INamedTypeSymbol serviceType,
        IReadOnlyList<INamedTypeSymbol> decorators,
        Location? location) =>
        CreateEntry(
            compilation,
            serviceType,
            decorators.Select(d => d.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToList(),
            decorators.ToList(),
            location,
            []);

    private static InterfaceDecoratorEntry CreateEntry(
        Compilation compilation,
        INamedTypeSymbol serviceType,
        IReadOnlyList<string> decoratorFqns,
        IReadOnlyList<INamedTypeSymbol> decoratorSymbols,
        Location? diagnosticLocation,
        IReadOnlyList<(string DecoratorFqn, Location? Location)> duplicateDecorators)
    {
        var assemblyKey = SymbolEqualityComparer.Default.Equals(serviceType.ContainingAssembly, compilation.Assembly)
            ? compilation.AssemblyName ?? "Servicefy"
            : serviceType.ContainingAssembly.Name;

        var typeNs = serviceType.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : assemblyKey;

        var interfaceFqn = serviceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new InterfaceDecoratorEntry(
            assemblyKey, typeNs, interfaceFqn, serviceType,
            decoratorFqns, decoratorSymbols, diagnosticLocation, duplicateDecorators);
    }
}
