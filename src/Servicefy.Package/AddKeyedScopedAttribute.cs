namespace Servicefy.Package;

public static class AddKeyedScopedAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddKeyedScopedAttribute<TService> : Attribute
                 {
                     public Type ServiceType { get; } = typeof(TService);
                     public object ServiceKey { get; }

                     public AddKeyedScopedAttribute(object serviceKey)
                     {
                         ServiceKey = serviceKey;
                     }
                 }

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddKeyedScopedAttribute : Attribute
                 {
                     public Type ServiceType { get; }
                     public object ServiceKey { get; }

                     public AddKeyedScopedAttribute(object serviceKey)
                     {
                         ServiceKey = serviceKey;
                     }

                     public AddKeyedScopedAttribute(object serviceKey, Type serviceType)
                     {
                         ServiceKey = serviceKey;
                         ServiceType = serviceType;
                     }
                 }
             }
             """;
    }
}
