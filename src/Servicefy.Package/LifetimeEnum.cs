namespace Servicefy.Package;

public static class LifetimeEnum
{
    public static string Value(string namespacedName)
        => $$"""
             namespace {{namespacedName}}
             {
                 internal enum Lifetime
                 {
                     Singleton = 0,
                     Scoped,
                     Transient
                 }
             }
             """;
}