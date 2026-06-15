---
title: Conventions
---

# Conventions

[← Back to Servicefy](../index.md)

Convention-based, zero-attribute service registration wich every call is resolved 
**at compile time** by the Servicefy source generator — no runtime reflection or assembly scanning.

```csharp
services.AddServicefyConventions()
    .ByNamespace(ns => ns.StartsWith("MyApp.Services"), Lifetime.Scoped)
    .ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)
    .ByBaseType<JobBase>(Lifetime.Singleton, ServiceTypeSelector.Self)
    .ByBaseType<IHandler>(Lifetime.Scoped, matchAttribute: typeof(HandlerAttribute))
    .ByNamespaceOf<SomeTypeInMyApp.Data.Marker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped)
    .ByTypeName((ns, name) => name.EndsWith("Repository") && ns.Equals("MyApp.Implementations"), Lifetime.Scoped);
```

## Conventions

| Convention | Description |
|------------|-------------|
| [`ByNamespace`](by-namespace.md) | Registers every type whose namespace matches a predicate, against its directly implemented interfaces. |
| [`ByBaseType`](by-base-type.md) | Registers every type assignable to `TBase`. |
| [`ByNamespaceOf`](by-namespace-of.md) | Like `ByNamespace`, but restricted to the namespace (or sub-namespaces) of a marker type `TMarker`. |
| [`ByTypeName`](by-type-name.md) | Registers every type whose namespace **and** type name match a two-parameter predicate. |

## Shared types

| Type | Used by | Description |
|------|---------|-------------|
| [`Lifetime`](../byconfiguration/lifetime.md) | Both | `Singleton` \| `Scoped` \| `Transient` |
| [`ServiceTypeSelector`](by-base-type.md#servicetypeselector) | `ByBaseType` | Controls which type(s) a matched class is registered as |

## Interop with ByConfiguration

A type already handled by [ByConfiguration](../byconfiguration/index.md) (i.e. annotated with
`[Add*]` / `[Configure]`) is **excluded** from convention matching — so a type is registered by exactly
one of the two generators, even when both `AddServicefy()` and `AddServicefyConventions()` are used in
the same project.

## Decorators

[`[DecoratorFor<T>]`](../byconfiguration/decorator-for.md) and `.Decorate<,>()` work the same way for
both conventions: if a matched type's interface has decorators, the full decorator chain is emitted
instead of a plain registration call. Classes used as `[DecoratorFor<T>]` / `.Decorate<,>()` targets
are excluded from matching.

## Entry point

```csharp
internal interface IServicefyConventionsBuilder
{
    IServiceCollection Services { get; }

    IServicefyConventionsBuilder ByNamespace(
        Func<string, bool> predicate,
        Lifetime lifetime,
        [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");

    IServicefyConventionsBuilder ByBaseType<TBase>(
        Lifetime lifetime,
        ServiceTypeSelector selector = ServiceTypeSelector.BaseType,
        Type matchAttribute = null);

    IServicefyConventionsBuilder ByNamespaceOf<TMarker>(
        Func<string, bool> predicate,
        Lifetime lifetime,
        [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");

    IServicefyConventionsBuilder ByTypeName(
        Func<string, string, bool> predicate,
        Lifetime lifetime,
        [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");
}
```

`AddServicefyConventions()` returns a new `IServicefyConventionsBuilder` wrapping your
`IServiceCollection`. Each call (`ByNamespace`, `ByBaseType<T>`, `ByNamespaceOf<TMarker>`,
`ByTypeName`) returns the same builder, so calls can be chained freely.
