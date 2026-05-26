namespace Servicefy.Package.ByConfiguration.Generators;

public static class ServicefyAggregatesAttribute
{
    public static string Value(string namespacedName)
        => $$"""
             using System;
             namespace {{namespacedName}}
             {
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                 public sealed class ServicefyAggregatesAttribute : Attribute
                 {
                     public string[] Namespaces { get; }

                     public ServicefyAggregatesAttribute(params string[] namespaces)
                     {
                         Namespaces = namespaces;
                     }
                 }
             }
             """;
}
