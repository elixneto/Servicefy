using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Servicefy.Package.Conventions.Generators.Emit;
using Servicefy.Package.Conventions.Models;
using Servicefy.Package.Decorators;
using Servicefy.Package.Diagnostics;

namespace Servicefy.Package.Conventions.ByBaseType.Generators.Emit;

/// <summary>
/// Implements <c>ServicefyConventionsBuilder.ApplyByBaseType</c> for every distinct
/// <c>.ByBaseType&lt;TBase&gt;(lifetime, selector, attribute)</c> call site found in the
/// compilation: matches types assignable to <c>TBase</c> (optionally filtered by attribute),
/// then registers them according to the requested <c>ServiceTypeSelector</c>.
/// </summary>
internal static class ByBaseTypeRegistrationEmitter
{
    internal static void Emit(
        SourceProductionContext spc,
        (Compilation Compilation, ImmutableArray<ByBaseTypeConventionRule> Rules) source)
    {
        var (compilation, rules) = source;

        var distinctRules = rules
            .DistinctBy(r => (r.BaseTypeFqn, r.Lifetime, r.Selector, r.AttributeFqn))
            .ToList();

        if (distinctRules.Count == 0) return;

        // SVCFY010: Self ignores TBase entirely and registers the matched concrete types as
        // themselves, so an interface TBase (which carries no usable "self" type) is rejected.
        distinctRules = distinctRules
            .Where(r =>
            {
                if (r.Selector != "Self" || r.BaseType.TypeKind != TypeKind.Interface) return true;

                spc.ReportDiagnostic(new SVCFY010(r.BaseTypeLocation).CreateDiagnostic(r.BaseTypeFqn));
                return false;
            })
            .ToList();

        if (distinctRules.Count == 0) return;

        var projectReferenceAssemblies = TypeCollector.GetProjectReferenceAssemblies(compilation)
            .ToHashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        var allTypes = TypeCollector.GetAllAccessibleTypes(compilation)
            .Concat(TypeCollector.GetExternalAccessibleTypes(compilation))
            .Select(t => t.Type)
            .ToList();

        var decoratorTypes = ConventionFilters.CollectDecoratorTypes(allTypes, compilation);

        var registrableTypes = allTypes
            .Where(t => IsRegistrable(t, decoratorTypes))
            .ToList();

        // Open-generic implementations (e.g. Repository<T> : IRepository<T>) are excluded from
        // registrableTypes (which only carries concrete, closed types) and matched separately:
        // they are registered against the unbound service via MS.DI's Add(typeof(IFoo<>), typeof(Impl<>)).
        var openRegistrableTypes = allTypes
            .Where(t => IsOpenGenericImplementation(t, decoratorTypes))
            .ToList();

        // First pass: compute matched types per rule and union all target service FQNs so
        // decorator validation runs once across the whole compilation (avoids duplicate
        // SVCFY007 diagnostics when multiple rules target overlapping interfaces).
        var ruleMatches = new List<(ByBaseTypeConventionRule Rule, List<INamedTypeSymbol> MatchedTypes, List<INamedTypeSymbol> OpenTypes)>();
        var registeredServices = new HashSet<string>(StringComparer.Ordinal);
        var closedServiceSymbols = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var rule in distinctRules)
        {
            var matchedTypes = registrableTypes
                .Where(t => IsAssignableTo(t, rule.BaseType, rule.IsOpenGeneric))
                .Where(t => rule.AttributeType is null || HasAttribute(t, rule.AttributeType))
                .OrderBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToList();

            // Open-generic passthrough only applies to the typeof(IFoo<>) overload.
            var matchedOpenTypes = rule.IsOpenGeneric
                ? openRegistrableTypes
                    .Where(t => IsAssignableTo(t, rule.BaseType, isOpenGeneric: true))
                    .Where(t => rule.AttributeType is null || HasAttribute(t, rule.AttributeType))
                    .OrderBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                    .ToList()
                : new List<INamedTypeSymbol>();

            if (matchedTypes.Count == 0 && matchedOpenTypes.Count == 0) continue;

            ruleMatches.Add((rule, matchedTypes, matchedOpenTypes));

            foreach (var service in GetTargetServiceSymbols(matchedTypes, rule, projectReferenceAssemblies))
            {
                registeredServices.Add(service.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                closedServiceSymbols.Add(service);
            }

            foreach (var impl in matchedOpenTypes)
            foreach (var service in GetOpenServiceForms(impl, rule, projectReferenceAssemblies))
                registeredServices.Add(UnboundFqn(service));
        }

        if (ruleMatches.Count == 0) return;

        var decoratorEntries = DecoratorCollector.CollectDecoratorEntries(compilation);

        // Expand open-generic .Decorate(typeof(IFoo<>), typeof(Decorator<>)) chains into closed
        // decorator entries for each closed service registered above (e.g. IRepository<Cliente> ->
        // LoggingRepository<Cliente>). Closed forms only — runtime-closed types reached through the
        // open passthrough cannot be decorated AOT-safely.
        SynthesizeOpenGenericDecoratorEntries(compilation, closedServiceSymbols, decoratorEntries);

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
        sb.AppendLine("        partial void ApplyByBaseType(Type baseType, Lifetime lifetime, ServiceTypeSelector selector, Type matchAttribute)");
        sb.AppendLine("        {");

        foreach (var (rule, matchedTypes, openTypes) in ruleMatches)
        {
            var attributeExpr = rule.AttributeFqn is null ? "null" : $"typeof({rule.AttributeFqn})";
            sb.AppendLine($"            if (baseType == typeof({rule.BaseTypeFqn}) && lifetime == Lifetime.{rule.Lifetime} && selector == ServiceTypeSelector.{rule.Selector} && matchAttribute == {attributeExpr})");
            sb.AppendLine("            {");

            EmitRegistrations(sb, "                ", rule, matchedTypes, openTypes, validDecoratorMap, projectReferenceAssemblies);

            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("ServicefyConventionsBuilder.ByBaseType.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitRegistrations(
        StringBuilder sb,
        string indent,
        ByBaseTypeConventionRule rule,
        List<INamedTypeSymbol> matchedTypes,
        List<INamedTypeSymbol> openTypes,
        Dictionary<string, InterfaceDecoratorEntry> validDecoratorMap,
        HashSet<IAssemblySymbol> projectReferenceAssemblies)
    {
        // Open-generic implementations are emitted first; the concrete, closed registrations below
        // are emitted after, so that for any closed service implemented by both an open impl and a
        // concrete type (e.g. IRepository<Cliente> from Repository<T> and ClienteRepository), the
        // concrete registration is the one MS.DI resolves (last registration wins).
        if (openTypes.Count > 0)
        {
            EmitOpenRegistrations(sb, indent, rule, openTypes, projectReferenceAssemblies);
        }

        switch (rule.Selector)
        {
            case "BaseType":
                // For an open generic base, there is no single closed service type to register against:
                // each matched type is registered against the constructed form(s) it actually implements
                // (e.g. ClienteRepository -> IRepository<Cliente>).
                if (rule.IsOpenGeneric)
                {
                    // Group matched types by the constructed (closed) interface they implement, so a
                    // single implementation of a given closed service (e.g. IRepository<Cliente>) can
                    // still receive its decorator chain via EmitAgainstService.
                    var byService = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
                    var serviceOrder = new List<string>();
                    foreach (var type in matchedTypes)
                    foreach (var constructed in GetConstructedForms(type, rule.BaseType))
                    {
                        var serviceFqn = constructed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (!byService.TryGetValue(serviceFqn, out var impls))
                        {
                            byService[serviceFqn] = impls = new List<INamedTypeSymbol>();
                            serviceOrder.Add(serviceFqn);
                        }

                        impls.Add(type);
                    }

                    foreach (var serviceFqn in serviceOrder)
                        EmitAgainstService(sb, indent, rule.Lifetime, serviceFqn, byService[serviceFqn], validDecoratorMap);
                }
                else
                {
                    EmitAgainstService(sb, indent, rule.Lifetime, rule.BaseTypeFqn, matchedTypes, validDecoratorMap);
                }

                break;

            case "ImplementedInterfaces":
                EmitAgainstInterfaces(
                    sb, indent, rule, matchedTypes, validDecoratorMap, projectReferenceAssemblies,
                    ConventionFilters.GetRegistrableInterfaces);
                break;

            case "AllImplementedInterfaces":
                EmitAgainstInterfaces(
                    sb, indent, rule, matchedTypes, validDecoratorMap, projectReferenceAssemblies,
                    ConventionFilters.GetAllRegistrableInterfaces);
                break;

            case "Self":
                foreach (var type in matchedTypes)
                {
                    var implFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    sb.AppendLine($"{indent}Services.Add{rule.Lifetime}<{implFqn}>();");
                }

                break;

            case "SelfWithInterfaces":
                foreach (var type in matchedTypes)
                {
                    var implFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    sb.AppendLine($"{indent}Services.Add{rule.Lifetime}<{implFqn}>();");

                    foreach (var iface in ConventionFilters.GetRegistrableInterfaces(type, projectReferenceAssemblies))
                    {
                        var serviceFqn = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        sb.AppendLine($"{indent}Services.Add{rule.Lifetime}<{serviceFqn}>(sp => sp.GetRequiredService<{implFqn}>());");
                    }
                }

                break;
        }
    }

    private static void EmitAgainstInterfaces(
        StringBuilder sb,
        string indent,
        ByBaseTypeConventionRule rule,
        List<INamedTypeSymbol> matchedTypes,
        Dictionary<string, InterfaceDecoratorEntry> validDecoratorMap,
        HashSet<IAssemblySymbol> projectReferenceAssemblies,
        Func<INamedTypeSymbol, HashSet<IAssemblySymbol>, IEnumerable<INamedTypeSymbol>> interfaceSelector)
    {
        var ifaceCounts = matchedTypes
            .SelectMany(t => interfaceSelector(t, projectReferenceAssemblies))
            .GroupBy(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var type in matchedTypes)
        {
            var implFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            foreach (var iface in interfaceSelector(type, projectReferenceAssemblies))
            {
                var serviceFqn = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (ifaceCounts[serviceFqn] == 1 && validDecoratorMap.TryGetValue(serviceFqn, out var entry))
                    DecoratorEmitter.EmitDecoratedService(sb, indent, "Services", rule.Lifetime, serviceFqn, implFqn, entry);
                else
                    sb.AppendLine($"{indent}Services.Add{rule.Lifetime}<{serviceFqn}, {implFqn}>();");
            }
        }
    }

    // Emits the open-generic passthrough registrations for a typeof(IFoo<>) rule:
    //   Services.Add{Lifetime}(typeof(IRepository<>), typeof(Repository<>));
    // which lets MS.DI resolve any IRepository<X> to Repository<X> — including closed types that
    // do not appear anywhere in the compilation. The service form(s) depend on the selector.
    private static void EmitOpenRegistrations(
        StringBuilder sb,
        string indent,
        ByBaseTypeConventionRule rule,
        List<INamedTypeSymbol> openTypes,
        HashSet<IAssemblySymbol> projectReferenceAssemblies)
    {
        foreach (var impl in openTypes)
        {
            var implUnbound = UnboundFqn(impl);

            // Self / SelfWithInterfaces register the open implementation as itself. Forwarding an
            // open generic to its interfaces via a factory isn't expressible, so SelfWithInterfaces
            // registers only the self form for the open part.
            if (rule.Selector is "Self" or "SelfWithInterfaces")
            {
                sb.AppendLine($"{indent}Services.Add{rule.Lifetime}(typeof({implUnbound}));");
                continue;
            }

            foreach (var service in GetOpenServiceForms(impl, rule, projectReferenceAssemblies))
                sb.AppendLine($"{indent}Services.Add{rule.Lifetime}(typeof({UnboundFqn(service)}), typeof({implUnbound}));");
        }
    }

    // The unbound service definition(s) an open implementation is registered against, per selector.
    // Only generic, user-defined services whose type arguments are exactly the implementation's own
    // type parameters (in order) qualify — the shape MS.DI's open-generic mapping requires.
    private static IEnumerable<INamedTypeSymbol> GetOpenServiceForms(
        INamedTypeSymbol impl, ByBaseTypeConventionRule rule, HashSet<IAssemblySymbol> projectReferenceAssemblies)
    {
        switch (rule.Selector)
        {
            case "BaseType":
                if (GetConstructedForms(impl, rule.BaseType).Any(f => UsesImplTypeParameters(f, impl)))
                    yield return rule.BaseType.OriginalDefinition;

                break;

            case "ImplementedInterfaces":
                foreach (var iface in impl.Interfaces)
                    if (IsOpenRegistrableInterface(iface, impl, projectReferenceAssemblies))
                        yield return iface.OriginalDefinition;

                break;

            case "AllImplementedInterfaces":
                foreach (var iface in impl.AllInterfaces)
                    if (IsOpenRegistrableInterface(iface, impl, projectReferenceAssemblies))
                        yield return iface.OriginalDefinition;

                break;
        }
    }

    private static bool IsOpenRegistrableInterface(
        INamedTypeSymbol iface, INamedTypeSymbol impl, HashSet<IAssemblySymbol> projectReferenceAssemblies) =>
        iface.IsGenericType
        && ConventionFilters.IsUserDefinedInterface(iface, projectReferenceAssemblies)
        && UsesImplTypeParameters(iface, impl);

    // True when a constructed form's type arguments are exactly <paramref name="impl"/>'s own type
    // parameters, in order — the condition under which MS.DI's open-generic registration maps a
    // requested IFoo<X> cleanly to Impl<X>.
    private static bool UsesImplTypeParameters(INamedTypeSymbol constructed, INamedTypeSymbol impl)
    {
        if (constructed.TypeArguments.Length != impl.TypeParameters.Length) return false;

        for (var i = 0; i < constructed.TypeArguments.Length; i++)
            if (!SymbolEqualityComparer.Default.Equals(constructed.TypeArguments[i], impl.TypeParameters[i]))
                return false;

        return true;
    }

    private static string UnboundFqn(INamedTypeSymbol type) =>
        type.OriginalDefinition.ConstructUnboundGenericType()
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static void EmitAgainstService(
        StringBuilder sb,
        string indent,
        string lifetime,
        string serviceFqn,
        List<INamedTypeSymbol> matchedTypes,
        Dictionary<string, InterfaceDecoratorEntry> validDecoratorMap)
    {
        if (matchedTypes.Count == 1 && validDecoratorMap.TryGetValue(serviceFqn, out var entry))
        {
            var implFqn = matchedTypes[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            DecoratorEmitter.EmitDecoratedService(sb, indent, "Services", lifetime, serviceFqn, implFqn, entry);
            return;
        }

        foreach (var type in matchedTypes)
        {
            var implFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            sb.AppendLine($"{indent}Services.Add{lifetime}<{serviceFqn}, {implFqn}>();");
        }
    }

    private static void SynthesizeOpenGenericDecoratorEntries(
        Compilation compilation,
        HashSet<INamedTypeSymbol> closedServiceSymbols,
        List<InterfaceDecoratorEntry> decoratorEntries)
    {
        var openChains = DecoratorCollector.CollectOpenGenericChains(compilation);
        if (openChains.Count == 0) return;

        // An explicit closed .Decorate<IRepository<Cliente>, X>() takes precedence over the expanded
        // open-generic chain for the same closed service.
        var existingFqns = decoratorEntries
            .Select(e => e.InterfaceFqn)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (unboundService, unboundDecorators, location) in openChains)
        foreach (var closed in closedServiceSymbols)
        {
            if (!closed.IsGenericType || closed.IsUnboundGenericType) continue;
            if (!SymbolEqualityComparer.Default.Equals(closed.OriginalDefinition, unboundService)) continue;

            var fqn = closed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!existingFqns.Add(fqn)) continue;

            var typeArgs = closed.TypeArguments;
            var closedDecorators = new List<INamedTypeSymbol>();
            var valid = true;
            foreach (var decorator in unboundDecorators)
            {
                // The decorator must close over the same type arguments as the service
                // (Decorator<T> : IFoo<T>); a differing arity can't be constructed positionally.
                if (decorator.Arity != typeArgs.Length)
                {
                    valid = false;
                    break;
                }

                closedDecorators.Add(decorator.Construct(typeArgs.ToArray()));
            }

            if (valid && closedDecorators.Count > 0)
                decoratorEntries.Add(DecoratorCollector.CreateEntry(compilation, closed, closedDecorators, location));
        }
    }

    // The closed service type symbols a rule registers its concrete (closed) matched types against.
    // Used for decorator wiring and to seed the registered-service set (the open-generic passthrough
    // services are unbound and contributed separately).
    private static IEnumerable<INamedTypeSymbol> GetTargetServiceSymbols(
        List<INamedTypeSymbol> matchedTypes, ByBaseTypeConventionRule rule, HashSet<IAssemblySymbol> projectReferenceAssemblies)
    {
        switch (rule.Selector)
        {
            case "BaseType":
                if (rule.IsOpenGeneric)
                {
                    foreach (var type in matchedTypes)
                    foreach (var constructed in GetConstructedForms(type, rule.BaseType))
                        yield return constructed;
                }
                else
                {
                    yield return rule.BaseType;
                }

                break;

            case "ImplementedInterfaces":
                foreach (var type in matchedTypes)
                foreach (var iface in ConventionFilters.GetRegistrableInterfaces(type, projectReferenceAssemblies))
                    yield return iface;

                break;

            case "AllImplementedInterfaces":
                foreach (var type in matchedTypes)
                foreach (var iface in ConventionFilters.GetAllRegistrableInterfaces(type, projectReferenceAssemblies))
                    yield return iface;

                break;
        }
    }

    private static bool IsRegistrable(INamedTypeSymbol type, HashSet<INamedTypeSymbol> decoratorTypes) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsStatic
        && type.TypeParameters.Length == 0
        && !ConventionFilters.InfrastructureTypeNames.Contains(type.Name)
        && !ConventionFilters.HasByConfigurationAttribute(type)
        && !decoratorTypes.Contains(type);

    // A concrete open-generic class (Repository<T>) eligible for open-generic registration. Mirrors
    // IsRegistrable but for types that still carry type parameters.
    private static bool IsOpenGenericImplementation(INamedTypeSymbol type, HashSet<INamedTypeSymbol> decoratorTypes) =>
        type.TypeKind == TypeKind.Class
        && !type.IsAbstract
        && !type.IsStatic
        && type.TypeParameters.Length > 0
        && !ConventionFilters.InfrastructureTypeNames.Contains(type.Name)
        && !ConventionFilters.HasByConfigurationAttribute(type)
        && !decoratorTypes.Contains(type);

    private static bool IsAssignableTo(INamedTypeSymbol type, INamedTypeSymbol baseType, bool isOpenGeneric)
    {
        // Open generic base: a type matches when any of its interfaces / base types is a constructed
        // form of the same unbound definition (IRepository<Cliente> matches typeof(IRepository<>)).
        if (isOpenGeneric)
        {
            var definition = baseType.OriginalDefinition;
            if (baseType.TypeKind == TypeKind.Interface)
                return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, definition));

            return WalkBaseTypes(type).Any(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, definition));
        }

        if (baseType.TypeKind == TypeKind.Interface)
            return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, baseType));

        return WalkBaseTypes(type).Any(t => SymbolEqualityComparer.Default.Equals(t, baseType));
    }

    // The constructed form(s) of an open generic base that <paramref name="type"/> implements or
    // derives from — e.g. ClienteRepository -> [IRepository<Cliente>]. A type can produce several
    // (IRepository<A> and IRepository<B>) when it closes the same definition more than once.
    private static IEnumerable<INamedTypeSymbol> GetConstructedForms(INamedTypeSymbol type, INamedTypeSymbol openGenericBase)
    {
        var definition = openGenericBase.OriginalDefinition;
        if (openGenericBase.TypeKind == TypeKind.Interface)
            return type.AllInterfaces.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, definition));

        return WalkBaseTypes(type).Where(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, definition));
    }

    private static IEnumerable<INamedTypeSymbol> WalkBaseTypes(INamedTypeSymbol type)
    {
        INamedTypeSymbol? current = type;
        while (current is not null)
        {
            yield return current;
            current = current.BaseType;
        }
    }

    private static bool HasAttribute(INamedTypeSymbol type, INamedTypeSymbol attributeType) =>
        type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));
}
