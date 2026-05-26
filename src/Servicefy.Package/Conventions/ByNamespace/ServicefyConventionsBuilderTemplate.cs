namespace Servicefy.Package.Conventions.ByNamespace;

/// <summary>
/// Runtime API emitted into every consuming assembly: <c>AddServicefyConventions()</c>,
/// <c>IServicefyConventionsBuilder</c> and the <c>ServicefyConventionsBuilder</c> partial class.
/// <c>ByNamespace</c> delegates to a partial method that <see cref="Emit.ByNamespaceRegistrationEmitter"/>
/// implements with the actual <c>AddScoped/AddSingleton/AddTransient</c> calls, dispatched by the
/// source text of the predicate lambda (captured via <c>CallerArgumentExpression</c>).
/// </summary>
internal static class ServicefyConventionsBuilderTemplate
{
    public static string Value(string namespacedName)
        => $$"""
             #pragma warning disable CS8618
             using System;
             using System.Runtime.CompilerServices;
             using Microsoft.Extensions.DependencyInjection;

             namespace {{namespacedName}}
             {
                 /// <summary>
                 /// Fluent builder for convention-based, zero-reflection service registration.
                 /// Obtained via <see cref="ServicefyConventionsExtensions.AddServicefyConventions"/>.
                 /// </summary>
                 internal interface IServicefyConventionsBuilder
                 {
                     /// <summary>The underlying service collection being configured.</summary>
                     IServiceCollection Services { get; }

                     /// <summary>
                     /// Registers every concrete, non-abstract, non-generic class whose namespace matches
                     /// <paramref name="predicate"/> against each of its directly implemented interfaces
                     /// (one registration per interface), using the given <paramref name="lifetime"/>.
                     /// Classes with no interfaces, abstract/static classes, and open generics are skipped.
                     /// </summary>
                     /// <param name="predicate">
                     /// A single-parameter lambda with an expression body, e.g.
                     /// <c>nm =&gt; nm.StartsWith("MyApp.Services")</c>. Supports
                     /// <c>StartsWith</c>/<c>EndsWith</c>/<c>Contains</c>/<c>Equals</c> (optionally with
                     /// <see cref="StringComparison"/>), <c>==</c>/<c>!=</c> against string literals,
                     /// <c>!</c>, <c>&amp;&amp;</c>, <c>||</c>, and parentheses. <c>StartsWith</c>/<c>EndsWith</c>/<c>Contains</c>
                     /// also accept multiple string-literal values to match against, either as separate
                     /// arguments — e.g. <c>nm =&gt; nm.EndsWith("Repository", "Handler")</c> — or as an
                     /// array/collection expression (C# 12+) — e.g.
                     /// <c>nm =&gt; nm.EndsWith(["Repository", "Handler"])</c> — optionally preceded by a
                     /// <see cref="StringComparison"/> (see <see cref="ServicefyStringExtensions"/>);
                     /// matches if any value matches. A non-literal value reports SVCFY011. Any other
                     /// shape fails to parse and the call site is ignored (no registration, no diagnostic).
                     /// </param>
                     /// <param name="lifetime">The lifetime used for every matched registration.</param>
                     /// <returns>The same builder, for chaining additional convention calls.</returns>
                     /// <seealso href="https://elixneto.github.io/Servicefy/conventions/by-namespace">ByNamespace — Servicefy docs</seealso>
                     IServicefyConventionsBuilder ByNamespace(
                         Func<string, bool> predicate,
                         Lifetime lifetime,
                         [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");

                     /// <summary>
                     /// Registers every concrete, non-abstract, non-generic class assignable to
                     /// <typeparamref name="TBase"/> (Scrutor-style "assignable to" scanning), using the
                     /// given <paramref name="lifetime"/> and <paramref name="selector"/>.
                     /// </summary>
                     /// <typeparam name="TBase">
                     /// The base type or interface to scan for. If an interface, matches are found via
                     /// <c>type.AllInterfaces</c> (covers inherited/transitive and closed generic
                     /// interfaces). If a class, matches are found by walking the base-type chain
                     /// (the type itself included). <typeparamref name="TBase"/> = <see cref="object"/>
                     /// matches every concrete class in the assembly — use with care.
                     /// </typeparam>
                     /// <param name="lifetime">The lifetime used for every matched registration.</param>
                     /// <param name="selector">
                     /// Controls which type(s) each matched class is registered as. Defaults to
                     /// <see cref="ServiceTypeSelector.BaseType"/>.
                     /// </param>
                     /// <param name="matchAttribute">
                     /// When set, only candidates with this attribute applied <b>directly</b> to the
                     /// concrete class (no inheritance) are matched.
                     /// </param>
                     /// <returns>The same builder, for chaining additional convention calls.</returns>
                     /// <seealso href="https://elixneto.github.io/Servicefy/conventions/by-base-type">ByBaseType — Servicefy docs</seealso>
                     IServicefyConventionsBuilder ByBaseType<TBase>(
                         Lifetime lifetime,
                         ServiceTypeSelector selector = ServiceTypeSelector.BaseType,
                         Type matchAttribute = null);

                     /// <summary>
                     /// Like <see cref="ByNamespace"/>, but candidates are first restricted to the
                     /// namespace of <typeparamref name="TMarker"/> (or one of its sub-namespaces)
                     /// before <paramref name="predicate"/> is evaluated. Useful for scoping a
                     /// convention to a specific module/feature area without hardcoding its
                     /// namespace as a string.
                     /// </summary>
                     /// <typeparam name="TMarker">
                     /// Any type whose containing namespace defines the root of the scan. Only types
                     /// in that namespace or a sub-namespace of it (e.g. <c>Root</c>,
                     /// <c>Root.Repositories</c>, ...) are considered, then filtered further by
                     /// <paramref name="predicate"/>.
                     /// </typeparam>
                     /// <param name="predicate">
                     /// Same shape as <see cref="ByNamespace"/>'s predicate, evaluated against each
                     /// remaining candidate's full namespace.
                     /// </param>
                     /// <param name="lifetime">The lifetime used for every matched registration.</param>
                     /// <returns>The same builder, for chaining additional convention calls.</returns>
                     /// <seealso href="https://elixneto.github.io/Servicefy/conventions/by-namespace-of">ByNamespaceOf — Servicefy docs</seealso>
                     IServicefyConventionsBuilder ByNamespaceOf<TMarker>(
                         Func<string, bool> predicate,
                         Lifetime lifetime,
                         [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");

                     /// <summary>
                     /// Registers every concrete, non-abstract, non-generic class whose namespace and
                     /// type name satisfy <paramref name="predicate"/> against each of its directly
                     /// implemented interfaces (same multi-interface behavior as <see cref="ByNamespace"/>).
                     /// </summary>
                     /// <param name="predicate">
                     /// A two-parameter lambda with an expression body, <c>(ns, name) =&gt; ...</c>,
                     /// where <c>ns</c> is the candidate's full namespace and <c>name</c> is its simple
                     /// type name. Supports the same operators as <see cref="ByNamespace"/>'s predicate
                     /// (<c>StartsWith</c>/<c>EndsWith</c>/<c>Contains</c>/<c>Equals</c>, optionally with
                     /// <see cref="StringComparison"/>, <c>==</c>/<c>!=</c>, <c>!</c>, <c>&amp;&amp;</c>,
                     /// <c>||</c>, and parentheses), applied to either parameter — including the
                     /// multi-value form of <c>StartsWith</c>/<c>EndsWith</c>/<c>Contains</c>, e.g.
                     /// <c>(ns, name) =&gt; name.EndsWith("Repository", "Handler")</c> or
                     /// <c>(ns, name) =&gt; name.EndsWith(["Repository", "Handler"])</c> (C# 12+). Any
                     /// other shape fails to parse and the call site is ignored (no registration, no
                     /// diagnostic).
                     /// </param>
                     /// <param name="lifetime">The lifetime used for every matched registration.</param>
                     /// <returns>The same builder, for chaining additional convention calls.</returns>
                     /// <seealso href="https://elixneto.github.io/Servicefy/conventions/by-type-name">ByTypeName — Servicefy docs</seealso>
                     IServicefyConventionsBuilder ByTypeName(
                         Func<string, string, bool> predicate,
                         Lifetime lifetime,
                         [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");

                     /// <summary>
                     /// Adds <typeparamref name="TDecorator"/> as an additional outer decorator layer for
                     /// <typeparamref name="TService"/>. See <c>DecoratorForAttribute&lt;TService&gt;</c> for the
                     /// attribute-based alternative and how the two combine.
                     /// </summary>
                     /// <returns>The same builder, for chaining additional convention calls.</returns>
                     IServicefyConventionsBuilder Decorate<TService, TDecorator>();
                 }

                 /// <summary>
                 /// Default <see cref="IServicefyConventionsBuilder"/> implementation. The
                 /// <c>ApplyByNamespace</c>/<c>ApplyByBaseType</c> partial methods are implemented by the
                 /// Servicefy source generator with the actual <c>Add{Lifetime}</c> calls for every call
                 /// site found in the compilation.
                 /// </summary>
                 internal sealed partial class ServicefyConventionsBuilder : IServicefyConventionsBuilder
                 {
                     /// <inheritdoc />
                     public IServiceCollection Services { get; }

                     internal ServicefyConventionsBuilder(IServiceCollection services)
                     {
                         Services = services;
                     }

                     /// <inheritdoc />
                     public IServicefyConventionsBuilder ByNamespace(
                         Func<string, bool> predicate,
                         Lifetime lifetime,
                         [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "")
                     {
                         ApplyByNamespace(lifetime, predicateExpression);
                         return this;
                     }

                     /// <inheritdoc />
                     public IServicefyConventionsBuilder ByBaseType<TBase>(
                         Lifetime lifetime,
                         ServiceTypeSelector selector = ServiceTypeSelector.BaseType,
                         Type matchAttribute = null)
                     {
                         ApplyByBaseType(typeof(TBase), lifetime, selector, matchAttribute);
                         return this;
                     }

                     /// <inheritdoc />
                     public IServicefyConventionsBuilder ByNamespaceOf<TMarker>(
                         Func<string, bool> predicate,
                         Lifetime lifetime,
                         [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "")
                     {
                         ApplyByNamespaceOf(typeof(TMarker), lifetime, predicateExpression);
                         return this;
                     }

                     /// <inheritdoc />
                     public IServicefyConventionsBuilder ByTypeName(
                         Func<string, string, bool> predicate,
                         Lifetime lifetime,
                         [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "")
                     {
                         ApplyByTypeName(lifetime, predicateExpression);
                         return this;
                     }

                     /// <inheritdoc />
                     public IServicefyConventionsBuilder Decorate<TService, TDecorator>() => this;

                     partial void ApplyByNamespace(Lifetime lifetime, string predicateExpression);

                     partial void ApplyByBaseType(Type baseType, Lifetime lifetime, ServiceTypeSelector selector, Type matchAttribute);

                     partial void ApplyByNamespaceOf(Type markerType, Lifetime lifetime, string predicateExpression);

                     partial void ApplyByTypeName(Lifetime lifetime, string predicateExpression);
                 }

                 /// <summary>
                 /// Entry point for Servicefy's convention-based registration.
                 /// </summary>
                 internal static class ServicefyConventionsExtensions
                 {
                     /// <summary>
                     /// Starts a fluent chain of convention-based registrations (<c>ByNamespace</c>,
                     /// <c>ByBaseType</c>) for <paramref name="services"/>.
                     /// </summary>
                     /// <param name="services">The service collection to configure.</param>
                     /// <returns>A new <see cref="IServicefyConventionsBuilder"/> wrapping <paramref name="services"/>.</returns>
                     /// <seealso href="https://elixneto.github.io/Servicefy/conventions/">Conventions — Servicefy docs</seealso>
                     public static IServicefyConventionsBuilder AddServicefyConventions(this IServiceCollection services)
                         => new ServicefyConventionsBuilder(services);
                 }

                 /// <summary>
                 /// String-matching helpers for <c>ByNamespace</c>/<c>ByNamespaceOf</c>/<c>ByTypeName</c>
                 /// predicates: match against any of several prefixes/suffixes/substrings without
                 /// chaining <c>||</c>, e.g. <c>name.EndsWith("Repository", "Handler", "Query")</c> or
                 /// <c>name.EndsWith(["Repository", "Handler", "Query"])</c> (C# 12+).
                 /// Only literal arguments (params or array/collection-expression) are understood by the
                 /// generator — anything else makes the call site ignored (SVCFY011).
                 /// </summary>
                 internal static class ServicefyStringExtensions
                 {
                     /// <summary>Returns <c>true</c> if <paramref name="value"/> starts with any of <paramref name="values"/> (ordinal comparison).</summary>
                     public static bool StartsWith(this string value, params string[] values)
                         => StartsWith(value, StringComparison.Ordinal, values);

                     /// <summary>Returns <c>true</c> if <paramref name="value"/> starts with any of <paramref name="values"/>.</summary>
                     public static bool StartsWith(this string value, StringComparison comparison, params string[] values)
                     {
                         foreach (var candidate in values)
                             if (value.StartsWith(candidate, comparison))
                                 return true;

                         return false;
                     }

                     /// <summary>Returns <c>true</c> if <paramref name="value"/> ends with any of <paramref name="values"/> (ordinal comparison).</summary>
                     public static bool EndsWith(this string value, params string[] values)
                         => EndsWith(value, StringComparison.Ordinal, values);

                     /// <summary>Returns <c>true</c> if <paramref name="value"/> ends with any of <paramref name="values"/>.</summary>
                     public static bool EndsWith(this string value, StringComparison comparison, params string[] values)
                     {
                         foreach (var candidate in values)
                             if (value.EndsWith(candidate, comparison))
                                 return true;

                         return false;
                     }

                     /// <summary>Returns <c>true</c> if <paramref name="value"/> contains any of <paramref name="values"/> (ordinal comparison).</summary>
                     public static bool Contains(this string value, params string[] values)
                         => Contains(value, StringComparison.Ordinal, values);

                     /// <summary>Returns <c>true</c> if <paramref name="value"/> contains any of <paramref name="values"/>.</summary>
                     public static bool Contains(this string value, StringComparison comparison, params string[] values)
                     {
                         foreach (var candidate in values)
                             if (value.Contains(candidate, comparison))
                                 return true;

                         return false;
                     }
                 }
             }
             """;
}
