namespace Servicefy.Package;

public static class ServiceTypeSelectorEnum
{
    public static string Value(string namespacedName)
        => $$"""
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Controls which type(s) a matched class is registered as when using
                 /// <c>AddServicefyConventions().ByBaseType&lt;TBase&gt;(...)</c>.
                 /// </summary>
                 /// <seealso href="https://elixneto.github.io/Servicefy/conventions/by-base-type">ByBaseType — Servicefy docs</seealso>
                 internal enum ServiceTypeSelector
                 {
                     /// <summary>
                     /// Registers <c>Services.Add{Lifetime}&lt;TBase, TImpl&gt;()</c> for each matched type. Default.
                     /// </summary>
                     BaseType = 0,

                     /// <summary>
                     /// Registers <c>Services.Add{Lifetime}&lt;IFoo, TImpl&gt;()</c> for each directly
                     /// implemented interface of every matched type.
                     /// </summary>
                     ImplementedInterfaces,

                     /// <summary>
                     /// Registers <c>Services.Add{Lifetime}&lt;IFoo, TImpl&gt;()</c> for every user-defined
                     /// interface in each matched type's full interface set — including inherited and
                     /// closed-generic interfaces (e.g. both <c>IClienteRepository</c> and the constructed
                     /// <c>IRepository&lt;Cliente&gt;</c>). Unlike <see cref="ImplementedInterfaces"/>, which
                     /// only registers the interfaces declared directly on the type.
                     /// </summary>
                     AllImplementedInterfaces,

                     /// <summary>
                     /// Registers <c>Services.Add{Lifetime}&lt;TImpl&gt;()</c> — the matched type with itself
                     /// as the service type.
                     /// </summary>
                     Self,

                     /// <summary>
                     /// Registers <c>Services.Add{Lifetime}&lt;TImpl&gt;()</c> plus
                     /// <c>Services.Add{Lifetime}&lt;IFoo&gt;(sp =&gt; sp.GetRequiredService&lt;TImpl&gt;())</c>
                     /// for each directly implemented interface.
                     /// </summary>
                     SelfWithInterfaces
                 }
             }
             """;
}
