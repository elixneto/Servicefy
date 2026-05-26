namespace Servicefy.Package.Decorators;

internal static class DecoratorAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             using System;
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Marks this class as a decorator for the single interface it implements — equivalent to
                 /// <c>[DecoratorFor&lt;TService&gt;]</c> with <c>TService</c> inferred from the decorator's
                 /// own implemented interface. Combined with any <c>.Decorate&lt;TService, TDecorator&gt;()</c>
                 /// calls for the same service: attribute-based decorators form the inner/base layer
                 /// (unspecified relative order), and <c>.Decorate&lt;,&gt;()</c> calls add outer layers in
                 /// declaration order (first call = outermost).
                 /// </summary>
                 /// <exception cref="ArgumentException">
                 /// <b>SVCFY014</b> — reported at compile time if this class does not implement exactly one
                 /// interface. Use <c>[DecoratorFor&lt;TService&gt;]</c> instead to specify the target
                 /// interface explicitly.
                 /// </exception>
                 /// <exception cref="ArgumentException">
                 /// <b>SVCFY007</b> — reported at compile time if this class has no public constructor with a
                 /// parameter of the decorated interface type.
                 /// </exception>
                 /// <exception cref="ArgumentException">
                 /// <b>SVCFY008</b> — reported at compile time if this class is itself registered with a
                 /// non-keyed <c>[Add*]</c> attribute.
                 /// </exception>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                 internal sealed class DecoratorAttribute : Attribute { }
             }
             """;
    }
}
