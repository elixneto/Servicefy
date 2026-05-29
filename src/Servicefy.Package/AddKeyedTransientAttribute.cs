namespace Servicefy.Package;

public static class AddKeyedTransientAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddKeyedTransientAttribute<TService> : Attribute
                 {
                     public Type ServiceType { get; } = typeof(TService);
                     public object ServiceKey { get; }

                     public AddKeyedTransientAttribute(object serviceKey)
                     {
                         ServiceKey = serviceKey;
                     }
                 }

                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class AddKeyedTransientAttribute : Attribute
                 {
                     public Type ServiceType { get; }
                     public object ServiceKey { get; }

                     public AddKeyedTransientAttribute(object serviceKey)
                     {
                         ServiceKey = serviceKey;
                     }

                     public AddKeyedTransientAttribute(object serviceKey, Type serviceType)
                     {
                         ServiceKey = serviceKey;
                         ServiceType = serviceType;
                     }
                 }
             }
             """;
    }
}
