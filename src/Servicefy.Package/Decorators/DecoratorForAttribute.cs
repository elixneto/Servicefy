namespace Servicefy.Package.Decorators;

internal static class DecoratorForAttribute
{
    public static string Value(string namespacedName)
    {
        return $$"""
             using System;
             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Marks this class as a decorator for <typeparamref name="TService"/>. Apply multiple times
                 /// to decorate multiple interfaces with the same class. Combined with any
                 /// <c>.Decorate&lt;TService, TDecorator&gt;()</c> calls for the same <typeparamref name="TService"/>:
                 /// attribute-based decorators form the inner/base layer (unspecified relative order), and
                 /// <c>.Decorate&lt;,&gt;()</c> calls add outer layers in declaration order (first call = outermost).
                 /// </summary>
                 /// <typeparam name="TService">The interface this class decorates.</typeparam>
                 /// <exception cref="ArgumentException">
                 /// <b>SVCFY007</b> — reported at compile time if this class has no public constructor with a
                 /// parameter of type <typeparamref name="TService"/>.
                 /// </exception>
                 /// <exception cref="ArgumentException">
                 /// <b>SVCFY008</b> — reported at compile time if this class is itself registered with a
                 /// non-keyed <c>[Add*]</c> attribute.
                 /// </exception>
                 [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                 internal sealed class DecoratorForAttribute<TService> : Attribute { }
             }
             """;
    }
}
