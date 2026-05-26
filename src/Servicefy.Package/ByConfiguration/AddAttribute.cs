namespace Servicefy.Package.ByConfiguration;

public static class AddAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                /// <summary>
                /// Registers the decorated class with an explicit <see cref="Lifetime"/> against the
                /// explicit service type <typeparamref name="TService"/>.
                /// </summary>
                /// <typeparam name="TService">The service type to register the class as.</typeparam>
                /// <exception cref="ArgumentException">
                /// <b>SVCFY002</b> — reported at compile time if the class does not implement <typeparamref name="TService"/>.
                /// </exception>
                /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/add">Add — Servicefy docs</seealso>
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                internal sealed class AddAttribute<TService> : Attribute
                {
                    /// <summary>The service type this registration targets.</summary>
                    public Type ServiceType { get; } = typeof(TService);

                    /// <summary>The registration lifetime.</summary>
                    public Lifetime Lifetime { get; }

                    /// <summary>
                    /// Registers the decorated class as <typeparamref name="TService"/> with the given
                    /// <paramref name="lifetime"/>.
                    /// </summary>
                    /// <param name="lifetime">The desired registration lifetime.</param>
                    /// <exception cref="ArgumentException">
                    /// <b>SVCFY001</b> — reported at compile time if <paramref name="lifetime"/> is omitted.
                    /// </exception>
                    public AddAttribute(Lifetime lifetime)
                    {
                        Lifetime = lifetime;
                    }
                }

                /// <summary>
                /// Registers the decorated class with an explicit <see cref="Lifetime"/>.
                /// With no explicit service type, registers against every directly implemented interface
                /// (one registration per interface). With an explicit <see cref="ServiceType"/>, registers
                /// against that type only.
                /// </summary>
                /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/add">Add — Servicefy docs</seealso>
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                internal sealed class AddAttribute : Attribute
                {
                    /// <summary>
                    /// The explicit service type, or <see langword="null"/> to register against all
                    /// directly implemented interfaces.
                    /// </summary>
                    public Type ServiceType { get; }

                    /// <summary>The registration lifetime.</summary>
                    public Lifetime Lifetime { get; }

                    /// <summary>
                    /// Registers against every directly implemented interface with the given
                    /// <paramref name="lifetime"/>.
                    /// </summary>
                    /// <param name="lifetime">The desired registration lifetime.</param>
                    /// <exception cref="ArgumentException">
                    /// <b>SVCFY001</b> — reported at compile time if <paramref name="lifetime"/> is omitted.
                    /// </exception>
                    /// <exception cref="ArgumentException">
                    /// <b>SVCFY003</b> — reported at compile time if the class implements no interfaces.
                    /// </exception>
                    public AddAttribute(Lifetime lifetime)
                    {
                        Lifetime = lifetime;
                    }

                    /// <summary>
                    /// Registers explicitly as <paramref name="serviceType"/> with the given
                    /// <paramref name="lifetime"/>.
                    /// </summary>
                    /// <param name="lifetime">The desired registration lifetime.</param>
                    /// <param name="serviceType">An interface or base type implemented by the decorated class.</param>
                    /// <exception cref="ArgumentException">
                    /// <b>SVCFY002</b> — reported at compile time if the class does not implement <paramref name="serviceType"/>.
                    /// </exception>
                    public AddAttribute(Lifetime lifetime, Type serviceType)
                    {
                        Lifetime = lifetime;
                        ServiceType = serviceType;
                    }
                }
             }
             """;
    }
}
