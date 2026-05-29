namespace Servicefy.Package;

public static class AddSingletonAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddSingletonAttribute<TService> : Attribute
                 {
                     public Type ServiceType { get; } = typeof(TService);
                 }

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddSingletonAttribute : Attribute
                 {
                     public Type ServiceType { get; }

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
