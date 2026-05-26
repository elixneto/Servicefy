namespace Servicefy.Package.ByConfiguration;

public static class AddScopedAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Registers the decorated class with a <b>Scoped</b> lifetime against the explicit
                 /// service type <typeparamref name="TService"/>.
                 /// </summary>
                 /// <typeparam name="TService">The service type to register the class as.</typeparam>
                 /// <exception cref="ArgumentException">
                 /// <b>SVCFY002</b> — reported at compile time if the class does not implement <typeparamref name="TService"/>.
                 /// </exception>
                 /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/add-scoped">AddScoped — Servicefy docs</seealso>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddScopedAttribute<TService> : Attribute
                 {
                     /// <summary>The service type this registration targets.</summary>
                     public Type ServiceType { get; } = typeof(TService);
                 }

                 /// <summary>
                 /// Registers the decorated class with a <b>Scoped</b> lifetime.
                 /// With no arguments, registers against every directly implemented interface
                 /// (one registration per interface). With an explicit <see cref="ServiceType"/>,
                 /// registers against that type only.
                 /// </summary>
                 /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/add-scoped">AddScoped — Servicefy docs</seealso>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddScopedAttribute : Attribute
                 {
                     /// <summary>
                     /// The explicit service type, or <see langword="null"/> to register against all
                     /// directly implemented interfaces.
                     /// </summary>
                     public Type ServiceType { get; }

                     /// <summary>
                     /// Registers against every directly implemented interface.
                     /// </summary>
                     /// <exception cref="ArgumentException">
                     /// <b>SVCFY003</b> — reported at compile time if the class implements no interfaces.
                     /// </exception>
                     public AddScopedAttribute() { }

                     /// <summary>
                     /// Registers explicitly as <paramref name="serviceType"/>.
                     /// </summary>
                     /// <param name="serviceType">An interface or base type implemented by the decorated class.</param>
                     /// <exception cref="ArgumentException">
                     /// <b>SVCFY002</b> — reported at compile time if the class does not implement <paramref name="serviceType"/>.
                     /// </exception>
                     public AddScopedAttribute(Type serviceType)
                     {
                         ServiceType = serviceType;
                     }
                 }
             }
             """;
    }
}
