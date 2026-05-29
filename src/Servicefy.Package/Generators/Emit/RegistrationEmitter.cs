using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Servicefy.Package.Generators.Analysis;
using Servicefy.Package.Generators.Models;

namespace Servicefy.Package.Generators.Emit;

/// <summary>
/// Orchestrates registration collection (via <see cref="TypeCollector"/>,
/// <see cref="AttributeParser"/> and <see cref="AggregationScanner"/>) and emits
/// one <c>ServicefyExtensions.*.g.cs</c> file per assembly key found.
/// </summary>
internal static class RegistrationEmitter
{
    internal static void Emit(SourceProductionContext spc, Compilation compilation)
    {
        var (serviceRegs, keyedRegs, configRegs) = CollectRegistrations(compilation);
        var referencedNamespaces = AggregationScanner.GetReferencedServicefyNamespaces(compilation);

        var assemblyKeys = serviceRegs.Select(r => r.AssemblyKey)
            .Concat(keyedRegs.Select(r => r.AssemblyKey))
            .Concat(configRegs.Select(r => r.AssemblyKey))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (assemblyKeys.Count == 0 && referencedNamespaces.Count == 0)
            return;

        // Pure-aggregator: no local registrations but has downstream Servicefy references.
        // We still need to emit a ServicefyExtensions that chains into them.
        var currentKey = compilation.AssemblyName ?? "Servicefy";
        if (referencedNamespaces.Count > 0 && !assemblyKeys.Contains(currentKey, StringComparer.Ordinal))
            assemblyKeys.Add(currentKey);

        foreach (var assemblyKey in assemblyKeys)
            EmitForAssembly(spc, assemblyKey, serviceRegs, keyedRegs, configRegs, referencedNamespaces);
    }

    // -------------------------------------------------------------------------
    // Collection
    // -------------------------------------------------------------------------

    private static (
        List<ServiceRegistration> Service,
        List<KeyedServiceRegistration> Keyed,
        List<ConfigurationRegistration> Config)
    CollectRegistrations(Compilation compilation)
    {
        var service = new List<ServiceRegistration>();
        var keyed   = new List<KeyedServiceRegistration>();
        var config  = new List<ConfigurationRegistration>();

        foreach (var (type, assemblyKey) in TypeCollector.GetAllAccessibleTypes(compilation))
        {
            var typeNs = type.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : assemblyKey;

            foreach (var attr in type.GetAttributes())
                CollectFromAttribute(attr, type, assemblyKey, typeNs, compilation, service, keyed, config);
        }

        return (service, keyed, config);
    }

    private static void CollectFromAttribute(
        AttributeData attr,
        INamedTypeSymbol type,
        string assemblyKey,
        string typeNs,
        Compilation compilation,
        List<ServiceRegistration> service,
        List<KeyedServiceRegistration> keyed,
        List<ConfigurationRegistration> config)
    {
        var implFqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (AttributeParser.TryGetServiceRegistration(attr, out var lifetime, out var explicitType))
        {
            if (lifetime is null) return;

            explicitType ??= AttributeParser.TryGetServiceTypeFromTypeofSyntax(attr, compilation);
            var serviceFqn = ResolveServiceType(type, explicitType);
            if (serviceFqn is null) return;

            service.Add(new ServiceRegistration(assemblyKey, typeNs, implFqn, serviceFqn, lifetime));
        }
        else if (AttributeParser.TryGetKeyedServiceRegistration(attr, out lifetime, out var key, out explicitType))
        {
            if (lifetime is null || key is null) return;

            explicitType ??= AttributeParser.TryGetKeyedServiceTypeFromArgs(attr, compilation);
            var serviceFqn = ResolveServiceType(type, explicitType);
            if (serviceFqn is null) return;

            keyed.Add(new KeyedServiceRegistration(assemblyKey, typeNs, implFqn, serviceFqn, lifetime, key));
        }
        else if (AttributeParser.TryGetConfigureRegistration(attr, out var section, out lifetime))
        {
            if (section is null || lifetime is null) return;

            var hasDefaultCtor = !type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared)
                                 || type.InstanceConstructors.Any(c => c.Parameters.Length == 0);
            if (!hasDefaultCtor) return;

            config.Add(new ConfigurationRegistration(assemblyKey, typeNs, implFqn, section, lifetime));
        }
    }

    /// <summary>
    /// Returns the fully-qualified service type name, or <c>null</c> when the
    /// registration should be skipped (interface not implemented or ambiguous).
    /// </summary>
    private static string? ResolveServiceType(INamedTypeSymbol impl, INamedTypeSymbol? explicitType)
    {
        if (explicitType is not null)
        {
            return impl.AllInterfaces.Contains(explicitType, SymbolEqualityComparer.Default)
                ? explicitType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;
        }

        if (impl.Interfaces.Length != 1) return null;
        return impl.Interfaces[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    // -------------------------------------------------------------------------
    // Emission
    // -------------------------------------------------------------------------

    private static void EmitForAssembly(
        SourceProductionContext spc,
        string assemblyKey,
        List<ServiceRegistration> allService,
        List<KeyedServiceRegistration> allKeyed,
        List<ConfigurationRegistration> allConfig,
        List<string> referencedNamespaces)
    {
        var asmService = allService
            .Where(r => r.AssemblyKey == assemblyKey)
            .DistinctBy(r => (r.Impl, r.Service, r.Lifetime))
            .ToList();

        var asmKeyed = allKeyed
            .Where(r => r.AssemblyKey == assemblyKey)
            .DistinctBy(r => (r.Impl, r.Service, r.Lifetime, r.Key))
            .ToList();

        var asmConfig = allConfig
            .Where(r => r.AssemblyKey == assemblyKey)
            .DistinctBy(r => (r.Impl, r.Section, r.Lifetime))
            .ToList();

        var ns = ResolveOutputNamespace(assemblyKey, allService, allKeyed, allConfig);

        var source = BuildSource(ns, asmService, asmKeyed, asmConfig, referencedNamespaces);
        spc.AddSource(
            $"ServicefyExtensions.{assemblyKey.Replace('.', '_')}.g.cs",
            SourceText.From(source, Encoding.UTF8));
    }

    private static string ResolveOutputNamespace(
        string assemblyKey,
        List<ServiceRegistration> service,
        List<KeyedServiceRegistration> keyed,
        List<ConfigurationRegistration> config)
    {
        var namespaces = service.Where(r => r.AssemblyKey == assemblyKey).Select(r => r.Namespace)
            .Concat(keyed.Where(r => r.AssemblyKey == assemblyKey).Select(r => r.Namespace))
            .Concat(config.Where(r => r.AssemblyKey == assemblyKey).Select(r => r.Namespace));

        return FindCommonNamespace(namespaces) is { Length: > 0 } common ? common : assemblyKey;
    }

    private static string BuildSource(
        string ns,
        List<ServiceRegistration> service,
        List<KeyedServiceRegistration> keyed,
        List<ConfigurationRegistration> config,
        List<string> referencedNamespaces)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.Configuration;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");

        if (referencedNamespaces.Count > 0)
        {
            var quoted = string.Join(", ", referencedNamespaces.Select(n => $"\"{n}\""));
            sb.AppendLine($"    [ServicefyAggregates({quoted})]");
        }

        sb.AppendLine("    public static class ServicefyExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        public static IServiceCollection AddServicefy(this IServiceCollection services, IConfiguration configuration)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (services is null) throw new ArgumentNullException(nameof(services));");
        sb.AppendLine("            if (configuration is null) throw new ArgumentNullException(nameof(configuration));");
        sb.AppendLine();

        foreach (var r in service)
            sb.AppendLine($"            services.Add{r.Lifetime}<{r.Service}, {r.Impl}>();");

        foreach (var r in keyed)
            sb.AppendLine($"            services.AddKeyed{r.Lifetime}<{r.Service}, {r.Impl}>({r.Key});");

        foreach (var r in config)
        {
            var escaped = r.Section.Replace("\"", "\\\"");
            sb.AppendLine($"            services.Add{r.Lifetime}(_ => configuration.GetSection(\"{escaped}\").Get<{r.Impl}>() ?? throw new InvalidOperationException(\"Unable to bind section '{escaped}' to type {r.Impl}.\"));");
        }

        if (referencedNamespaces.Count > 0)
        {
            if (service.Count > 0 || keyed.Count > 0 || config.Count > 0)
                sb.AppendLine();

            foreach (var refNs in referencedNamespaces)
                sb.AppendLine($"            global::{refNs}.ServicefyExtensions.AddServicefy(services, configuration);");
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
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
}
