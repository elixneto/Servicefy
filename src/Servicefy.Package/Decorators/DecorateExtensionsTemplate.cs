namespace Servicefy.Package.Decorators;

internal static class DecorateExtensionsTemplate
{
    public static string Value(string namespacedName)
    {
        return $$"""
             using Microsoft.Extensions.DependencyInjection;
             namespace {{namespacedName}}
             {
                 internal static class ServicefyDecorateExtensions
                 {
                     /// <summary>
                     /// Marker call, resolved entirely at compile time by the Servicefy source generator:
                     /// adds <typeparamref name="TDecorator"/> as an additional outer decorator layer for
                     /// <typeparamref name="TService"/>. Declaration order across all
                     /// <c>.Decorate&lt;,&gt;()</c> calls for the same <typeparamref name="TService"/>
                     /// determines layering — the first call is the outermost layer.
                     /// </summary>
                     public static IServiceCollection Decorate<TService, TDecorator>(this IServiceCollection services)
                         => services;
                 }
             }
             """;
    }
}
