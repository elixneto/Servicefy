namespace Servicefy.Package;

public static class AddScopedAttribute
{
    public static string Value(string namespacedName, bool emitGeneric)
    {
        var generic = emitGeneric ? $$"""

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddScopedAttribute<TService> : Attribute
                 {
                     public Type ServiceType { get; } = typeof(TService);
                 }

         """ : "";

        return $$"""
             using System;
             namespace {{namespacedName}}
             {
                 {{generic}}
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddScopedAttribute : Attribute
                 {
                     public Type? ServiceType { get; }

                     public AddScopedAttribute() { }

                     public AddScopedAttribute(Type serviceType)
                     {
                         ServiceType = serviceType;
                     }
                 }
             }
             """;
    }
}
