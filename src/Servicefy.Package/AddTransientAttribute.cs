namespace Servicefy.Package;

public static class AddTransientAttribute
{
    public static string Value(string namespacedName, bool emitGeneric)
    {
        var generic = emitGeneric ? $$"""

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddTransientAttribute<TService> : Attribute
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
                 internal sealed class AddTransientAttribute : Attribute
                 {
                     public Type? ServiceType { get; }

                     public AddTransientAttribute() { }

                     public AddTransientAttribute(Type serviceType)
                     {
                         ServiceType = serviceType;
                     }
                 }
             }
             """;
    }
}
