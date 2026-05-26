namespace Servicefy.Package;

public static class LifetimeEnum
{
    public static string Value(string namespacedName)
        => $$"""
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// The lifetime used when registering a service with the
                 /// <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
                 /// </summary>
                 /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/lifetime">Lifetime — Servicefy docs</seealso>
                 internal enum Lifetime
                 {
                     /// <summary>One instance for the lifetime of the application (<c>AddSingleton</c>).</summary>
                     Singleton = 0,

                     /// <summary>One instance per request/scope (<c>AddScoped</c>).</summary>
                     Scoped,

                     /// <summary>A new instance every time it is requested (<c>AddTransient</c>).</summary>
                     Transient
                 }
             }
             """;
}
