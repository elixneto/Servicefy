namespace Servicefy.Package;

public static class AddTransientAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddTransientAttribute<TService> : Attribute
                 {
                     public Type ServiceType { get; } = typeof(TService);
                 }

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddTransientAttribute : Attribute
                 {
                     public Type ServiceType { get; }

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
