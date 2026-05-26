namespace Servicefy.Package;

public static class AddAttribute
{
    public static string Value(string namespacedName, bool emitGeneric)
    {
        var generic = emitGeneric ? $$"""

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

        """ : "";

        return $$"""
             using System;
             namespace {{namespacedName}}
             {
                {{generic}}
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                internal sealed class AddAttribute : Attribute
                {
                    public Type? ServiceType { get; }
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
