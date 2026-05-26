namespace Servicefy.Package.ByConfiguration;

public static class AddSelfAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             using System;
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Registers the decorated class with itself as the service type
                 /// (<c>services.Add{Lifetime}&lt;TImpl&gt;()</c>), ignoring any implemented interfaces.
                 /// </summary>
                 /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/add-self">AddSelf — Servicefy docs</seealso>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                 internal sealed class AddSelfAttribute : Attribute
                 {
                     /// <summary>The registration lifetime.</summary>
                     public Lifetime Lifetime { get; }

                     /// <summary>
                     /// Registers the decorated class with itself as the service type, using the given
                     /// <paramref name="lifetime"/>.
                     /// </summary>
                     /// <param name="lifetime">The desired registration lifetime.</param>
                     /// <exception cref="ArgumentException">
                     /// <b>SVCFY001</b> — reported at compile time if <paramref name="lifetime"/> is omitted.
                     /// </exception>
                     /// <exception cref="ArgumentException">
                     /// <b>SVCFY009</b> — reported at compile time if applied to an abstract or static class.
                     /// </exception>
                     public AddSelfAttribute(Lifetime lifetime)
                     {
                         Lifetime = lifetime;
                     }
                 }
             }
             """;
    }
}
