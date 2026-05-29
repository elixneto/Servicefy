namespace Servicefy.Package;

public static class AddAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             #pragma warning disable CS8618
             using System;
             namespace {{namespacedName}}
             {
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                internal sealed class AddAttribute<TService> : Attribute
                {
                    public Type ServiceType { get; } = typeof(TService);
                    public Lifetime Lifetime { get; }

                    public AddAttribute(Lifetime lifetime)
                    {
                        Lifetime = lifetime;
                    }
                }

                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                internal sealed class AddAttribute : Attribute
                {
                    public Type ServiceType { get; }
                    public Lifetime Lifetime { get; }

                    public AddAttribute(Lifetime lifetime)
                    {
                        Lifetime = lifetime;
                    }

                    public AddAttribute(Lifetime lifetime, Type serviceType)
                    {
                        Lifetime = lifetime;
                        ServiceType = serviceType;
                    }
                }
             }
             """;
    }
}
