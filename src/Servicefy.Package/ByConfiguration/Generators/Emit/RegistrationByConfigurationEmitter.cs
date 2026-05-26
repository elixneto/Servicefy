using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Servicefy.Package.ByConfiguration.Generators.Analysis;
using Servicefy.Package.ByConfiguration.Models;
using Servicefy.Package.Decorators;
using Servicefy.Package.Diagnostics;

namespace Servicefy.Package.ByConfiguration.Generators.Emit;

/// <summary>
/// Orchestrates registration collection (via <see cref="TypeCollector"/>,
/// <see cref="AttributeParser"/> and <see cref="AggregationScanner"/>) and emits
/// one <c>ServicefyExtensions.*.g.cs</c> file per assembly key found.
/// </summary>
internal static class RegistrationByConfigurationEmitter
{
    internal static void Emit(SourceProductionContext spc, Compilation compilation)
    {
        var (serviceRegs, keyedRegs, configRegs, decoratorEntries) = CollectRegistrations(compilation);
        var referencedNamespaces = AggregationScanner.GetReferencedServicefyNamespaces(compilation);

        var validDecoratorMap = ValidateAndBuildDecoratorMap(spc, serviceRegs, decoratorEntries);

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
            EmitForAssembly(spc, assemblyKey, serviceRegs, keyedRegs, configRegs, referencedNamespaces, validDecoratorMap);
    }

    // -------------------------------------------------------------------------
    // Collection
    // -------------------------------------------------------------------------

    private static (
        List<ServiceRegistration> Service,
        List<KeyedServiceRegistration> Keyed,
        List<ConfigurationRegistration> Config,
        List<InterfaceDecoratorEntry> Decorators)
    CollectRegistrations(Compilation compilation)
    {
        var service = new List<ServiceRegistration>();
        var keyed   = new List<KeyedServiceRegistration>();
        var config  = new List<ConfigurationRegistration>();

        foreach (var (type, assemblyKey) in TypeCollector.GetAllAccessibleTypes(compilation))
        {
            if (type.TypeKind == TypeKind.Interface) continue;

            var typeNs = type.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : assemblyKey;

            foreach (var attr in type.GetAttributes())
                CollectFromAttribute(attr, type, assemblyKey, typeNs, compilation, service, keyed, config);
        }

        var decorators = DecoratorCollector.CollectDecoratorEntries(compilation);

        return (service, keyed, config, decorators);
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
            var location = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation();
            foreach (var serviceFqn in ResolveServiceTypes(type, explicitType))
                service.Add(new ServiceRegistration(assemblyKey, typeNs, implFqn, serviceFqn, lifetime, location));
        }
        else if (AttributeParser.TryGetSelfServiceRegistration(attr, out lifetime))
        {
            if (lifetime is null) return;

            var location = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation();
            service.Add(new ServiceRegistration(assemblyKey, typeNs, implFqn, implFqn, lifetime, location));
        }
        else if (AttributeParser.TryGetKeyedServiceRegistration(attr, out lifetime, out var key, out explicitType))
        {
            if (lifetime is null || key is null) return;

            explicitType ??= AttributeParser.TryGetKeyedServiceTypeFromArgs(attr, compilation);
            foreach (var serviceFqn in ResolveServiceTypes(type, explicitType))
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
    /// Returns the fully-qualified service type names this registration should target.
    /// With an explicit type, returns that single type (or none if not implemented).
    /// Otherwise, returns one entry per interface directly implemented by <paramref name="impl"/>
    /// (none if it implements no interfaces).
    /// </summary>
    private static List<string> ResolveServiceTypes(INamedTypeSymbol impl, INamedTypeSymbol? explicitType)
    {
        if (explicitType is not null)
        {
            return impl.AllInterfaces.Contains(explicitType, SymbolEqualityComparer.Default)
                ? [explicitType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]
                : [];
        }

        return impl.Interfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private static Dictionary<string, InterfaceDecoratorEntry> ValidateAndBuildDecoratorMap(
        SourceProductionContext spc,
        List<ServiceRegistration> serviceRegs,
        List<InterfaceDecoratorEntry> decoratorEntries)
    {
        if (decoratorEntries.Count == 0) return new Dictionary<string, InterfaceDecoratorEntry>(StringComparer.Ordinal);

        // Build a lookup: decorator impl FQN → the interface it decorates (for SVCFY008)
        var decoratorToInterface = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in decoratorEntries)
        {
            foreach (var fqn in entry.DecoratorFqns)
                decoratorToInterface.TryAdd(fqn, entry.InterfaceFqn);
        }

        // SVCFY008: decorator class registered with a non-keyed [Add*]
        foreach (var reg in serviceRegs)
        {
            if (decoratorToInterface.TryGetValue(reg.Impl, out var decoratedInterface))
                spc.ReportDiagnostic(new SVCFY008(reg.Location).CreateDiagnostic(reg.Impl, decoratedInterface));
        }

        var registeredServices = serviceRegs.Select(r => r.Service).ToHashSet(StringComparer.Ordinal);
        return DecoratorEmitter.ValidateAndBuildDecoratorMap(spc, decoratorEntries, registeredServices);
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
        List<(string Namespace, bool RequiresConfiguration)> referencedNamespaces,
        Dictionary<string, InterfaceDecoratorEntry> decoratorMap)
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

        var source = BuildSource(ns, asmService, asmKeyed, asmConfig, referencedNamespaces, decoratorMap);
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
        List<(string Namespace, bool RequiresConfiguration)> referencedNamespaces,
        Dictionary<string, InterfaceDecoratorEntry> decoratorMap)
    {
        var needsConfiguration = config.Count > 0 || referencedNamespaces.Any(r => r.RequiresConfiguration);
        var needsOptions = config.Any(r => r.Lifetime != AttributeParser.SingletonLifetime);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        if (needsConfiguration)
            sb.AppendLine("using Microsoft.Extensions.Configuration;");
        if (needsOptions)
            sb.AppendLine("using Microsoft.Extensions.Options;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");

        if (referencedNamespaces.Count > 0)
        {
            var quoted = string.Join(", ", referencedNamespaces.Select(n => $"\"{n.Namespace}\""));
            sb.AppendLine($"    [ServicefyAggregates({quoted})]");
        }

        sb.AppendLine("    public static class ServicefyExtensions");
        sb.AppendLine("    {");

        var signature = needsConfiguration
            ? "AddServicefy(this IServiceCollection services, IConfiguration configuration)"
            : "AddServicefy(this IServiceCollection services)";
        sb.AppendLine($"        public static IServiceCollection {signature}");
        sb.AppendLine("        {");
        sb.AppendLine("            if (services is null) throw new ArgumentNullException(nameof(services));");
        if (needsConfiguration)
            sb.AppendLine("            if (configuration is null) throw new ArgumentNullException(nameof(configuration));");
        sb.AppendLine();

        foreach (var r in service)
        {
            if (r.Service == r.Impl)
                sb.AppendLine($"            services.Add{r.Lifetime}<{r.Impl}>();");
            else if (decoratorMap.TryGetValue(r.Service, out var decoratorEntry))
                DecoratorEmitter.EmitDecoratedService(sb, "            ", "services", r.Lifetime, r.Service, r.Impl, decoratorEntry);
            else
                sb.AppendLine($"            services.Add{r.Lifetime}<{r.Service}, {r.Impl}>();");
        }

        foreach (var r in keyed)
        {
            sb.AppendLine($"            services.AddKeyed{r.Lifetime}<{r.Service}, {r.Impl}>({r.Key});");
        }

        foreach (var r in config)
        {
            var escaped = r.Section.Replace("\"", "\\\"");
            if (r.Lifetime == AttributeParser.SingletonLifetime)
            {
                // Singletons are created once and never see a config reload, so IOptionsMonitor<T>
                // would add no value here — bind the section once at registration time.
                sb.AppendLine($"            services.AddSingleton(_ => configuration.GetSection(\"{escaped}\").Get<{r.Impl}>() ?? throw new InvalidOperationException(\"Unable to bind section '{escaped}' to type {r.Impl}.\"));");
            }
            else
            {
                // IOptionsMonitor<T> picks up appsettings.json reloads at runtime.
                sb.AppendLine($"            services.Configure<{r.Impl}>(configuration.GetSection(\"{escaped}\"));");
                sb.AppendLine($"            services.Add{r.Lifetime}(sp => sp.GetRequiredService<IOptionsMonitor<{r.Impl}>>().Value);");
            }
        }

        if (referencedNamespaces.Count > 0)
        {
            if (service.Count > 0 || keyed.Count > 0 || config.Count > 0)
            {
                sb.AppendLine();
            }

            foreach (var refNs in referencedNamespaces)
            {
                var args = refNs.RequiresConfiguration ? "services, configuration" : "services";
                sb.AppendLine($"            global::{refNs.Namespace}.ServicefyExtensions.AddServicefy({args});");
            }
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
