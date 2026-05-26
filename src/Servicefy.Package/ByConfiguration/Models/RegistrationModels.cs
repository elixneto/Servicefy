using Microsoft.CodeAnalysis;

namespace Servicefy.Package.ByConfiguration.Models;

/// <summary>Standard DI registration originating from [AddScoped], [AddSingleton], [AddTransient] or [Add].</summary>
internal sealed record ServiceRegistration(
    string AssemblyKey,
    string Namespace,
    string Impl,
    string Service,
    string Lifetime,
    Location? Location = null);

/// <summary>Keyed DI registration originating from [AddKeyedScoped], [AddKeyedSingleton] or [AddKeyedTransient].</summary>
internal sealed record KeyedServiceRegistration(
    string AssemblyKey,
    string Namespace,
    string Impl,
    string Service,
    string Lifetime,
    string Key);

/// <summary>Configuration-section binding originating from [Configure].</summary>
internal sealed record ConfigurationRegistration(
    string AssemblyKey,
    string Namespace,
    string Impl,
    string Section,
    string Lifetime);
