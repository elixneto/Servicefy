namespace Servicefy.Package;

public static class AddScopedAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddScopedAttribute<TService> : Attribute
                 {
                     public Type ServiceType { get; } = typeof(TService);
                 }

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddScopedAttribute : Attribute
                 {
                     public Type ServiceType { get; }

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
