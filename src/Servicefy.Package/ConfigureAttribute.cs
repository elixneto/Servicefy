namespace Servicefy.Package;

public static class ConfigureAttribute
{
    public static string Value(string namespacedName)
        => $$"""
             using System;
             namespace {{namespacedName}}
             {
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                 internal sealed class ConfigureAttribute : Attribute
                 {
                     public string SectionName { get; }
                     public Lifetime Lifetime { get; }

                     public ConfigureAttribute(string sectionName, Lifetime lifetime)
                     {
                         SectionName = sectionName;
                         Lifetime = lifetime;
                     }
                 }
             }
             """;
}