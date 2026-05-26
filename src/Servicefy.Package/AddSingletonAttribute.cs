namespace Servicefy.Package;

public static class AddSingletonAttribute
{
    public static string Value(string namespacedName, bool emitGeneric)
    {
        var generic = emitGeneric ? $$"""

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddSingletonAttribute<TService> : Attribute
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
                 internal sealed class AddSingletonAttribute : Attribute
                 {
                     public Type? ServiceType { get; }

                     public AddSingletonAttribute() { }

                     public AddSingletonAttribute(Type serviceType)
                     {
                         ServiceType = serviceType;
                     }
                 }
             }
             """;
    }
}
