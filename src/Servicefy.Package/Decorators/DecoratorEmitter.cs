using System.Text;
using Microsoft.CodeAnalysis;
using Servicefy.Package.ByConfiguration.Generators.Analysis;
using Servicefy.Package.Diagnostics;

namespace Servicefy.Package.Decorators;

/// <summary>
/// Validates <c>[DecoratorFor&lt;T&gt;]</c> / <c>.Decorate&lt;,&gt;()</c> entries (SVCFY007/SVCFY013) and
/// emits the keyed decorator chain (<c>AddKeyedXxx("__BASE__")</c> + factories + outer resolver).
/// Shared between ByConfiguration and ByConvention generators.
/// </summary>
internal static class DecoratorEmitter
{
    /// <summary>
    /// Validates each decorator entry whose interface is in <paramref name="registeredServices"/>
    /// (SVCFY013: decorator doesn't implement the decorated interface, SVCFY007: decorator has no
    /// constructor accepting the service interface type) and returns a map of interface FQN → entry
    /// for those that pass validation and can be emitted.
    /// </summary>
    internal static Dictionary<string, InterfaceDecoratorEntry> ValidateAndBuildDecoratorMap(
        SourceProductionContext spc,
        List<InterfaceDecoratorEntry> decoratorEntries,
        HashSet<string> registeredServices)
    {
        var validMap = new Dictionary<string, InterfaceDecoratorEntry>(StringComparer.Ordinal);

        foreach (var entry in decoratorEntries)
        {
            if (!registeredServices.Contains(entry.InterfaceFqn))
                continue;

            foreach (var (decoratorFqn, location) in entry.DuplicateDecorators)
            {
                spc.ReportDiagnostic(new SVCFY015(location ?? entry.DiagnosticLocation)
                    .CreateDiagnostic(decoratorFqn, entry.InterfaceFqn));
            }

            var allValid = true;
            for (var i = 0; i < entry.DecoratorSymbols.Count; i++)
            {
                if (!entry.DecoratorSymbols[i].AllInterfaces.Contains(entry.InterfaceSymbol, SymbolEqualityComparer.Default))
                {
                    spc.ReportDiagnostic(new SVCFY013(entry.DiagnosticLocation)
                        .CreateDiagnostic(entry.DecoratorFqns[i], entry.InterfaceFqn));
                    allValid = false;
                    continue;
                }

                var ctor = SelectConstructor(entry.DecoratorSymbols[i]);
                if (ctor is null || !ctor.Parameters.Any(p =>
                        SymbolEqualityComparer.Default.Equals(p.Type, entry.InterfaceSymbol)))
                {
                    spc.ReportDiagnostic(new SVCFY007(entry.DiagnosticLocation)
                        .CreateDiagnostic(entry.DecoratorFqns[i], entry.InterfaceFqn));
                    allValid = false;
                }
            }

            if (allValid)
                validMap[entry.InterfaceFqn] = entry;
        }

        return validMap;
    }

    internal static IMethodSymbol? SelectConstructor(INamedTypeSymbol type)
    {
        var publicCtors = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        if (publicCtors.Count == 0)
        {
            return null;
        }

        var preferred = publicCtors.FirstOrDefault(c =>
            c.GetAttributes().Any(a =>
                a.AttributeClass?.Name is "ActivatorUtilitiesConstructorAttribute" or "ActivatorUtilitiesConstructor"));
        if (preferred is not null)
        {
            return preferred;
        }

        if (publicCtors.Count == 1)
        {
            return publicCtors[0];
        }

        // Constructors where every parameter is DI-injectable (interface or non-primitive class).
        // This avoids selecting overloads with string/value-type parameters that cannot be resolved from DI.
        var injectable = publicCtors
            .Where(c => c.Parameters.All(p => IsInjectableType(p.Type)))
            .OrderByDescending(c => c.Parameters.Length)
            .ToList();

        return injectable.Count > 0
            ? injectable[0]
            : publicCtors.OrderByDescending(c => c.Parameters.Length).First();
    }

    private static bool IsInjectableType(ITypeSymbol type)
    {
        // Primitive/special types (string, int, bool, etc.) are not DI-injectable
        if (type.SpecialType != SpecialType.None) return false;
        // Value types (struct, enum) are not DI-injectable
        if (type.IsValueType) return false;
        return true;
    }

    /// <summary>
    /// Emits <c>{servicesExpr}.AddKeyedXxx&lt;TService, TImpl&gt;("__BASE__")</c> followed by one
    /// keyed factory per decorator (innermost first) and a final non-keyed resolver pointing at
    /// the outermost decorator (or "__BASE__" if there are none).
    /// </summary>
    internal static void EmitDecoratedService(
        StringBuilder sb,
        string indent,
        string servicesExpr,
        string lifetime,
        string serviceFqn,
        string implFqn,
        InterfaceDecoratorEntry entry)
    {
        var baseKey = "__BASE__";
        
        sb.AppendLine($@"{indent}{servicesExpr}.AddKeyed{lifetime}<{serviceFqn}, {implFqn}>(""{baseKey}"");");

        // Decorators are stored outermost→innermost (declaration order); apply in reverse so the
        // innermost wraps the base first, then each subsequent layer wraps the previous one.
        for (var i = entry.DecoratorFqns.Count - 1; i >= 0; i--)
        {
            var decoratorFqn = entry.DecoratorFqns[i];
            var decoratorSymbol = entry.DecoratorSymbols[i];
            var decoratorKey = decoratorFqn.StartsWith("global::") ? decoratorFqn[8..] : decoratorFqn;

            var factory = BuildDecoratorFactory(decoratorFqn, decoratorSymbol, entry.InterfaceSymbol, baseKey, serviceFqn);
            sb.AppendLine($"{indent}{servicesExpr}.AddKeyed{lifetime}<{serviceFqn}>(");
            sb.AppendLine($"{indent}    \"{decoratorKey}\",");
            sb.AppendLine($"{indent}    {factory});");

            baseKey = decoratorKey;
        }

        sb.AppendLine($"{indent}{servicesExpr}.Add{lifetime}<{serviceFqn}>(sp =>");
        sb.AppendLine($"{indent}    sp.GetRequiredKeyedService<{serviceFqn}>(\"{baseKey}\"));");
    }

    private static string BuildDecoratorFactory(
        string decoratorFqn,
        INamedTypeSymbol decoratorSymbol,
        INamedTypeSymbol interfaceSymbol,
        string innerKey,
        string serviceFqn)
    {
        var ctor = SelectConstructor(decoratorSymbol)!;
        var injectedInner = false;

        var args = ctor.Parameters.Select(p =>
        {
            if (!injectedInner && SymbolEqualityComparer.Default.Equals(p.Type, interfaceSymbol))
            {
                injectedInner = true;
                return $"sp.GetRequiredKeyedService<{serviceFqn}>(\"{innerKey}\")";
            }

            var fromKeyedAttr = p.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.Name is "FromKeyedServicesAttribute" or "FromKeyedServices");

            if (fromKeyedAttr is not null)
            {
                var keyArg = fromKeyedAttr.ConstructorArguments.ElementAtOrDefault(0);
                var keyStr = AttributeParser.FormatKeyConstant(keyArg);
                var pFqn = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"sp.GetRequiredKeyedService<{pFqn}>({keyStr ?? "null"})";
            }

            var typeFqn = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"sp.GetRequiredService<{typeFqn}>()";
        });

        return $"(sp, _) => new {decoratorFqn}({string.Join(", ", args)})";
    }
}