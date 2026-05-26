namespace Servicefy.Package.ByConfiguration;

public static class AddKeyedScopedAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Registers the decorated class as a <b>keyed Scoped</b> service against the explicit
                 /// service type <typeparamref name="TService"/>.
                 /// </summary>
                 /// <typeparam name="TService">The service type to register the class as.</typeparam>
                 /// <exception cref="ArgumentException">
                 /// <b>SVCFY002</b> — reported at compile time if the class does not implement <typeparamref name="TService"/>.
                 /// </exception>
                 /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/add-keyed-scoped">AddKeyedScoped — Servicefy docs</seealso>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddKeyedScopedAttribute<TService> : Attribute
                 {
                     /// <summary>The service type this registration targets.</summary>
                     public Type ServiceType { get; } = typeof(TService);

                     /// <summary>The key used to resolve this registration.</summary>
                     public object ServiceKey { get; }

                     /// <summary>
                     /// Registers the decorated class as a keyed service.
                     /// </summary>
                     /// <param name="serviceKey">The key used with <c>GetRequiredKeyedService</c> / <c>GetKeyedService</c>.</param>
                     public AddKeyedScopedAttribute(object serviceKey)
                     {
                         ServiceKey = serviceKey;
                     }
                 }

                 /// <summary>
                 /// Registers the decorated class as a <b>keyed Scoped</b> service.
                 /// With no explicit service type, registers against every directly implemented interface
                 /// (one registration per interface, sharing the same key). With an explicit
                 /// <see cref="ServiceType"/>, registers against that type only.
                 /// </summary>
                 /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/add-keyed-scoped">AddKeyedScoped — Servicefy docs</seealso>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddKeyedScopedAttribute : Attribute
                 {
                     /// <summary>
                     /// The explicit service type, or <see langword="null"/> to register against all
                     /// directly implemented interfaces.
                     /// </summary>
                     public Type ServiceType { get; }

                     /// <summary>The key used to resolve this registration.</summary>
                     public object ServiceKey { get; }

                     /// <summary>
                     /// Registers against every directly implemented interface as a keyed service.
                     /// </summary>
                     /// <param name="serviceKey">The key used with <c>GetRequiredKeyedService</c> / <c>GetKeyedService</c>.</param>
                     /// <exception cref="ArgumentException">
                     /// <b>SVCFY003</b> — reported at compile time if the class implements no interfaces.
                     /// </exception>
                     public AddKeyedScopedAttribute(object serviceKey)
                     {
                         ServiceKey = serviceKey;
                     }

                     /// <summary>
                     /// Registers explicitly as <paramref name="serviceType"/>, as a keyed service.
                     /// </summary>
                     /// <param name="serviceKey">The key used with <c>GetRequiredKeyedService</c> / <c>GetKeyedService</c>.</param>
                     /// <param name="serviceType">An interface or base type implemented by the decorated class.</param>
                     /// <exception cref="ArgumentException">
                     /// <b>SVCFY002</b> — reported at compile time if the class does not implement <paramref name="serviceType"/>.
                     /// </exception>
                     public AddKeyedScopedAttribute(object serviceKey, Type serviceType)
                     {
                         ServiceKey = serviceKey;
                         ServiceType = serviceType;
                     }
                 }
             }
             """;
    }
}
