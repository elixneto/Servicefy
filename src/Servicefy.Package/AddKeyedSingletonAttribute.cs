namespace Servicefy.Package;

public static class AddKeyedSingletonAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddKeyedSingletonAttribute<TService> : Attribute
                 {
                     public Type ServiceType { get; } = typeof(TService);
                     public object ServiceKey { get; }

                     public AddKeyedSingletonAttribute(object serviceKey)
                     {
                         ServiceKey = serviceKey;
                     }
                 }

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddKeyedSingletonAttribute : Attribute
                 {
                     public Type ServiceType { get; }
                     public object ServiceKey { get; }

                     public AddKeyedSingletonAttribute(object serviceKey)
                     {
                         ServiceKey = serviceKey;
                     }

                     public AddKeyedSingletonAttribute(object serviceKey, Type serviceType)
                     {
                         ServiceKey = serviceKey;
                         ServiceType = serviceType;
                     }
                 }
             }
             """;
    }
}
