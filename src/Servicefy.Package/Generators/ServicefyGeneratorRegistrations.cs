using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Servicefy.Package.Generators;

public static class ServicefyGeneratorRegistrations
{
    const string SingletonLifetime = "Singleton";
    const string ScopedLifetime = "Scoped";
    const string TransientLifetime = "Transient";

    public static void Emit(SourceProductionContext spc, Compilation compilation)
    {
        var serviceRegs = new List<(string AssemblyKey, string Namespace, string Impl, string Service, string Lifetime)>();
        var configSectionRegs = new List<(string AssemblyKey, string Namespace, string Impl, string Section, string Lifetime)>();

        foreach (var (type, assemblyKey) in GetAllAccessibleTypes(compilation))
        {
            var typeNamespace = type.ContainingNamespace is { IsGlobalNamespace: false } typeNs
                ? typeNs.ToDisplayString()
                : assemblyKey;

            foreach (var attr in type.GetAttributes())
            {
                if (TryGetServiceRegistration(attr, out var lifetime, out var explicitServiceType))
                {
                    if (lifetime is null) continue;

                    explicitServiceType ??= TryGetServiceTypeFromTypeofSyntax(attr, compilation);

                    if (explicitServiceType is not null)
                    {
                        if (!type.AllInterfaces.Contains(explicitServiceType, SymbolEqualityComparer.Default)) continue;
                        serviceRegs.Add((assemblyKey, typeNamespace,
                            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            explicitServiceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            lifetime));
                    }
                    else
                    {
                        if (type.Interfaces.Length != 1) continue;
                        var inferred = type.Interfaces[0];
                        serviceRegs.Add((assemblyKey, typeNamespace,
                            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            inferred.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            lifetime));
                    }
                }
                else if (TryGetConfigureRegistration(attr, out var sectionName, out lifetime))
                {
                    if (sectionName is null || lifetime is null) continue;

                    var hasParameterlessCtor = !type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared)
                                               || type.InstanceConstructors.Any(c => c.Parameters.Length == 0);

                    if (!hasParameterlessCtor) continue;

                    configSectionRegs.Add((assemblyKey, typeNamespace,
                        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        sectionName,
                        lifetime));
                }
            }
        }

        var referencedNamespaces = GetReferencedServicefyNamespaces(compilation);

        var assemblyKeys = serviceRegs.Select(r => r.AssemblyKey)
            .Concat(configSectionRegs.Select(r => r.AssemblyKey))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (assemblyKeys.Count == 0 && referencedNamespaces.Count == 0)
            return;

        // pure-aggregator (without own types but with Servicefy references),
        // ensure the current assembly has an entry to generate the ServicefyExtensions.
        var currentAssemblyKey = compilation.AssemblyName ?? "Servicefy";
        if (referencedNamespaces.Count > 0 && !assemblyKeys.Contains(currentAssemblyKey, StringComparer.Ordinal))
        {
            assemblyKeys.Add(currentAssemblyKey);
        }

        foreach (var assemblyKey in assemblyKeys)
        {
            var asmServiceRegs = serviceRegs
                .Where(r => r.AssemblyKey == assemblyKey)
                .Select(r => (r.Impl, r.Service, r.Lifetime))
                .Distinct(StringTupleComparer.Instance)
                .ToList();

            var asmConfigRegs = configSectionRegs
                .Where(r => r.AssemblyKey == assemblyKey)
                .Select(r => (r.Impl, r.Section, r.Lifetime))
                .Distinct(SectionTupleComparer.Instance)
                .ToList();

            var allNamespaces = serviceRegs
                .Where(r => r.AssemblyKey == assemblyKey).Select(r => r.Namespace)
                .Concat(configSectionRegs.Where(r => r.AssemblyKey == assemblyKey).Select(r => r.Namespace));

            var ns = FindCommonNamespace(allNamespaces) is { Length: > 0 } common
                ? common
                : assemblyKey;

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("using Microsoft.Extensions.Configuration;");
            sb.AppendLine();
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");

            if (referencedNamespaces.Count > 0)
            {
                var quotedNs = string.Join(", ", referencedNamespaces.Select(n => $"\"{n}\""));
                sb.AppendLine($"    [ServicefyAggregates({quotedNs})]");
            }

            sb.AppendLine("    public static class ServicefyExtensions");
            sb.AppendLine("    {");
            sb.AppendLine(
                "        public static IServiceCollection AddServicefy(this IServiceCollection services, IConfiguration configuration)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (services is null) throw new ArgumentNullException(nameof(services));");
            sb.AppendLine("            if (configuration is null) throw new ArgumentNullException(nameof(configuration));");
            sb.AppendLine();

            foreach (var reg in asmServiceRegs)
            {
                sb.AppendLine($"            services.Add{reg.Lifetime}<{reg.Service}, {reg.Impl}>();");
            }

            foreach (var reg in asmConfigRegs)
            {
                var escapedSection = reg.Section.Replace("\"", "\\\"");
                sb.AppendLine(
                    $"            services.Add{reg.Lifetime}(_ => configuration.GetSection(\"{escapedSection}\").Get<{reg.Impl}>() ?? throw new InvalidOperationException(\"Unable to bind section '{escapedSection}' to type {reg.Impl}.\"));");
            }

            if (referencedNamespaces.Count > 0)
            {
                if (asmServiceRegs.Count > 0 || asmConfigRegs.Count > 0)
                {
                    sb.AppendLine();
                }

                foreach (var refNs in referencedNamespaces)
                {
                    sb.AppendLine($"            global::{refNs}.ServicefyExtensions.AddServicefy(services, configuration);");
                }
            }

            sb.AppendLine("            return services;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            var safeKey = assemblyKey.Replace('.', '_');
            spc.AddSource($"ServicefyExtensions.{safeKey}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    private static IEnumerable<(INamedTypeSymbol Type, string AssemblyKey)> GetAllAccessibleTypes(Compilation compilation)
    {
        var currentKey = compilation.AssemblyName ?? "Servicefy";
        foreach (var type in GetAllTypesInNamespace(compilation.Assembly.GlobalNamespace))
            yield return (type, currentKey);
    }

    private static List<string> GetReferencedServicefyNamespaces(Compilation compilation)
    {
        var found = new List<(string Namespace, IReadOnlyList<string> Covers)>();

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol referencedAssembly)
                continue;

            var name = referencedAssembly.Name;
            if (name.StartsWith("System.", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                name is "System" or "mscorlib" or "netstandard" or "WindowsBase")
                continue;

            var entry = FindServicefyExtensionsEntry(referencedAssembly);
            if (entry is not null)
                found.Add(entry.Value);
        }

        // Filtra namespaces que já são cobertos por outro ServicefyExtensions encontrado.
        // Ex.: se SharedDI cobre Feature1 e Feature2, apenas SharedDI entra na lista final.
        var coveredByOthers = new HashSet<string>(
            found.SelectMany(f => f.Covers),
            StringComparer.Ordinal);

        return found
            .Where(f => !coveredByOthers.Contains(f.Namespace))
            .Select(f => f.Namespace)
            .ToList();
    }

    private static string FindCommonNamespace(IEnumerable<string> namespaces)
    {
        var parts = namespaces
            .Distinct(StringComparer.Ordinal)
            .Select(ns => ns.Split('.'))
            .ToList();

        if (parts.Count == 0) return string.Empty;

        var common = parts[0].ToList();
        foreach (var nsParts in parts.Skip(1))
        {
            var len = Math.Min(common.Count, nsParts.Length);
            common = common.Take(len).TakeWhile((seg, i) => seg == nsParts[i]).ToList();
            if (common.Count == 0) break;
        }

        return string.Join(".", common);
    }


    private static IEnumerable<INamedTypeSymbol> GetAllTypesInNamespace(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;

            foreach (var nestedType in GetNestedTypes(type))
                yield return nestedType;
        }

        foreach (var nested in ns.GetNamespaceMembers())
        foreach (var type in GetAllTypesInNamespace(nested))
            yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol symbol)
    {
        foreach (var nestedType in symbol.GetTypeMembers())
        {
            yield return nestedType;

            foreach (var deepType in GetNestedTypes(nestedType))
                yield return deepType;
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Impl, string Service, string Lifetime)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string Impl, string Service, string Lifetime) x, (string Impl, string Service, string Lifetime) y) =>
            x.Impl == y.Impl && x.Service == y.Service && x.Lifetime == y.Lifetime;

        public int GetHashCode((string Impl, string Service, string Lifetime) obj) =>
            ((obj.Impl?.GetHashCode() ?? 0) * 397) ^
            ((obj.Service?.GetHashCode() ?? 0) * 97) ^
            (obj.Lifetime?.GetHashCode() ?? 0);
    }

    private sealed class SectionTupleComparer : IEqualityComparer<(string Impl, string Section, string Lifetime)>
    {
        public static readonly SectionTupleComparer Instance = new();

        public bool Equals((string Impl, string Section, string Lifetime) x, (string Impl, string Section, string Lifetime) y) =>
            x.Impl == y.Impl && x.Section == y.Section && x.Lifetime == y.Lifetime;

        public int GetHashCode((string Impl, string Section, string Lifetime) obj) =>
            ((obj.Impl?.GetHashCode() ?? 0) * 397) ^
            ((obj.Section?.GetHashCode() ?? 0) * 97) ^
            (obj.Lifetime?.GetHashCode() ?? 0);
    }

    private static (string Namespace, IReadOnlyList<string> Covers)? FindServicefyExtensionsEntry(IAssemblySymbol assembly) =>
        SearchNamespaceForServicefyExtensions(assembly.GlobalNamespace);

    private static (string Namespace, IReadOnlyList<string> Covers)? SearchNamespaceForServicefyExtensions(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers("ServicefyExtensions"))
        {
            if (type.IsStatic &&
                type.DeclaredAccessibility == Accessibility.Public &&
                HasConformingAddServicefyMethod(type))
            {
                var typeNs = type.ContainingNamespace is { IsGlobalNamespace: false } containingNs
                    ? containingNs.ToDisplayString()
                    : type.ContainingAssembly?.Name ?? string.Empty;

                var covers = ReadServicefyAggregates(type);
                return (typeNs, covers);
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            var found = SearchNamespaceForServicefyExtensions(child);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadServicefyAggregates(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "ServicefyAggregatesAttribute") continue;
            if (attr.ConstructorArguments.Length == 0) continue;

            var arg = attr.ConstructorArguments[0];
            if (arg.Kind == TypedConstantKind.Array)
            {
                return arg.Values
                    .Where(v => v.Value is string)
                    .Select(v => (string)v.Value!)
                    .ToList();
            }
        }

        return Array.Empty<string>();
    }

    private static bool HasConformingAddServicefyMethod(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers("AddServicefy"))
        {
            if (member is not IMethodSymbol method) continue;
            if (!method.IsStatic) continue;
            if (method.DeclaredAccessibility != Accessibility.Public) continue;
            if (method.Parameters.Length != 2) continue;
            if (method.Parameters[0].Type.Name != "IServiceCollection") continue;
            if (method.Parameters[1].Type.Name != "IConfiguration") continue;
            return true;
        }

        return false;
    }

    public static bool TryGetServiceRegistration(
        AttributeData attribute,
        out string? lifetime,
        out INamedTypeSymbol? explicitServiceType)
    {
        lifetime = null;
        explicitServiceType = null;

        var attrName = attribute.AttributeClass?.Name;
        switch (attrName)
        {
            case "AddAttribute":
            case "Add":
                lifetime = MapLifetime(attribute.ConstructorArguments.ElementAtOrDefault(0).Value)
                           ?? GetLifetimeFromSyntax(attribute, 0);
                break;
            case "AddScopedAttribute":
            case "AddScoped":
                lifetime = ScopedLifetime;
                break;
            case "AddSingletonAttribute":
            case "AddSingleton":
                lifetime = SingletonLifetime;
                break;
            case "AddTransientAttribute":
            case "AddTransient":
                lifetime = TransientLifetime;
                break;
            default:
                return false;
        }

        if (attribute.AttributeClass?.TypeArguments.Length > 0)
            explicitServiceType = attribute.AttributeClass.TypeArguments[0] as INamedTypeSymbol;

        return true;
    }

    public static bool TryGetConfigureRegistration(
        AttributeData attribute,
        out string? sectionName,
        out string? lifetime)
    {
        sectionName = null;
        lifetime = null;

        var attrName = attribute.AttributeClass?.Name;
        if (attrName is not ("ConfigureAttribute" or "Configure"))
            return false;

        sectionName = attribute.ConstructorArguments.ElementAtOrDefault(0).Value as string
                      ?? GetStringFromSyntax(attribute, 0);
        lifetime = MapLifetime(attribute.ConstructorArguments.ElementAtOrDefault(1).Value)
                   ?? GetLifetimeFromSyntax(attribute, 1);
        return true;
    }

    public static INamedTypeSymbol? TryGetServiceTypeFromTypeofSyntax(AttributeData attribute, Compilation compilation)
    {
        // Caso resolvido: o argumento typeof() já foi resolvido pelo Roslyn
        foreach (var arg in attribute.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Type && arg.Value is INamedTypeSymbol resolved)
                return resolved;
        }

        // Fallback via syntax: resolve typeof(T) usando o semantic model da syntax tree
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
            return null;

        var arguments = syntax.ArgumentList?.Arguments;
        if (arguments is null) return null;

        foreach (var arg in arguments)
        {
            if (arg.Expression is TypeOfExpressionSyntax typeOfExpr)
            {
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                return model.GetTypeInfo(typeOfExpr.Type).Type as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static string? GetLifetimeFromSyntax(AttributeData attribute, int argIndex)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
            return null;
        var argExpr = syntax.ArgumentList?.Arguments.ElementAtOrDefault(argIndex)?.Expression;
        if (argExpr is MemberAccessExpressionSyntax mae)
        {
            return mae.Name.Identifier.ValueText switch
            {
                "Singleton" => SingletonLifetime,
                "Scoped" => ScopedLifetime,
                "Transient" => TransientLifetime,
                _ => null
            };
        }

        return null;
    }

    private static string? GetStringFromSyntax(AttributeData attribute, int argIndex)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
            return null;
        var argExpr = syntax.ArgumentList?.Arguments.ElementAtOrDefault(argIndex)?.Expression;
        return argExpr is LiteralExpressionSyntax literal ? literal.Token.ValueText : null;
    }

    private static string? MapLifetime(object? value) => value switch
    {
        0 => SingletonLifetime,
        1 => ScopedLifetime,
        2 => TransientLifetime,
        _ => null
    };
}