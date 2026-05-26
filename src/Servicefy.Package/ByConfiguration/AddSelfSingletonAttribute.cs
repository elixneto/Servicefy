namespace Servicefy.Package.ByConfiguration;

public static class AddSelfSingletonAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             using System;
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Registers the decorated class with itself as the service type and a <b>Singleton</b>
                 /// lifetime (<c>services.AddSingleton&lt;TImpl&gt;()</c>), ignoring any implemented interfaces.
                 /// </summary>
                 /// <exception cref="ArgumentException">
                 /// <b>SVCFY009</b> — reported at compile time if applied to an abstract or static class.
                 /// </exception>
                 /// <seealso href="https://elixneto.github.io/Servicefy/byconfiguration/add-self-singleton">AddSelfSingleton — Servicefy docs</seealso>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                 internal sealed class AddSelfSingletonAttribute : Attribute
                 {
                 }
             }
             """;
    }
}
