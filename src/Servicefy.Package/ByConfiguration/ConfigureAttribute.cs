namespace Servicefy.Package.ByConfiguration;

public static class ConfigureAttribute
{
    public static string Value(string namespacedName)
        => $$"""
             using System;
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Binds a configuration section to the decorated class via
                 /// <c>services.Configure&lt;T&gt;(configuration.GetSection(sectionName))</c> and registers
                 /// the bound options with the given <see cref="Lifetime"/>.
                 /// </summary>
                 /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/configure">Configure — Servicefy docs</seealso>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                 internal sealed class ConfigureAttribute : Attribute
                 {
                     /// <summary>The configuration section name to bind.</summary>
                     public string SectionName { get; }

                     /// <summary>The lifetime used to register the bound options.</summary>
                     public Lifetime Lifetime { get; }

                     /// <summary>
                     /// Binds the configuration section <paramref name="sectionName"/> to the decorated class.
                     /// </summary>
                     /// <param name="sectionName">The name of the configuration section to bind.</param>
                     /// <param name="lifetime">The desired registration lifetime for the bound options.</param>
                     /// <exception cref="ArgumentException">
                     /// <b>SVCFY005</b> — reported at compile time if <paramref name="sectionName"/> or
                     /// <paramref name="lifetime"/> is omitted.
                     /// </exception>
                     /// <exception cref="ArgumentException">
                     /// <b>SVCFY006</b> — reported at compile time if the class has no parameterless constructor.
                     /// </exception>
                     public ConfigureAttribute(string sectionName, Lifetime lifetime)
                     {
                         SectionName = sectionName;
                         Lifetime = lifetime;
                     }
                 }
             }
             """;
}
