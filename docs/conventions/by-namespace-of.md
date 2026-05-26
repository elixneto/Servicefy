---
title: ByNamespaceOf
---

# ByNamespaceOf

[← Back to Conventions](index.md)

Like [`ByNamespace`](by-namespace.md), but candidates are first restricted to the namespace of
`TMarker` (or one of its sub-namespaces) before the predicate runs — avoids hardcoding a namespace
prefix as a string.

```csharp
services.AddServicefyConventions()
    .ByNamespaceOf<SomeTypeInMyApp.Data.Marker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped);
```

## Signature

```csharp
IServicefyConventionsBuilder ByNamespaceOf<TMarker>(
    Func<string, bool> predicate,
    Lifetime lifetime,
    [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");
```

| Parameter | Description |
|-----------|-------------|
| `TMarker` | Any type whose containing namespace defines the **root** of the scan. See [Matching](#matching) below. |
| `predicate` | A single-parameter lambda with an expression body — same shape as [ByNamespace](by-namespace.md#supported-predicates), evaluated against each remaining candidate's full namespace. |
| `lifetime` | The [`Lifetime`](../byconfiguration/lifetime.md) used for every matched registration. |

**Returns:** the same builder, for chaining additional convention calls.

## Matching

The root namespace is `typeof(TMarker).Namespace`. A candidate type is considered only if its
namespace **equals the root** or **starts with `root + "."`** — i.e. it is the root namespace itself
or one of its sub-namespaces. The `predicate` is then evaluated against that candidate's full
namespace, exactly like [ByNamespace](by-namespace.md).

```csharp
namespace MyApp.Data
{
    public class Marker { }
}

namespace MyApp.Data.Repositories
{
    public interface IUserRepository { }
    public class UserRepository : IUserRepository { }
}

namespace MyApp.Other.Repositories
{
    public interface IOrderRepository { }
    public class OrderRepository : IOrderRepository { }
}
```

```csharp
services.AddServicefyConventions()
    .ByNamespaceOf<MyApp.Data.Marker>(ns => ns.Contains("Repositories"), Lifetime.Scoped);
```

Only `UserRepository` is registered. `OrderRepository` lives under `MyApp.Other.Repositories`, which
satisfies the predicate but is **outside** `MyApp.Data`'s namespace subtree, so it is excluded before
the predicate is even evaluated.

If `TMarker` is itself declared in the **global namespace**, the namespace restriction is a no-op and
`ByNamespaceOf` behaves exactly like [`ByNamespace`](by-namespace.md).

## What gets registered

Same rules as [ByNamespace](by-namespace.md#what-gets-registered): classes with zero implemented
interfaces, abstract/static classes, and open generics are skipped; classes with 2+ interfaces are
registered against all of them.

## Decorators

Same shared decorator chain as [ByNamespace](by-namespace.md#decorators) — if a matched type's
interface has decorators ([`[DecoratorFor<T>]`](../byconfiguration/decorator-for.md) and/or
`.Decorate<,>()`), the full decorator chain is emitted instead of a plain `Add{Lifetime}<IFoo, TImpl>()`.

## Interop with ByConfiguration

Types already annotated with a [ByConfiguration](../byconfiguration/index.md) attribute (`[Add*]`,
`[Configure]`) are excluded from `ByNamespaceOf` matching, same as `ByNamespace`.

## How it works (dispatch)

`ByNamespaceOf<TMarker>(...)` calls a generator-implemented partial method,
`ApplyByNamespaceOf(typeof(TMarker), lifetime, predicateExpression)`. The generator emits one
`if (markerType == typeof(...) && lifetime == Lifetime.X && predicateExpression == "...") { ...; return; }`
branch per distinct call site found in the compilation. If a rule matches zero types, no branch is
emitted for it — and if **no** rule matches anything, no file is emitted at all (same as
[ByNamespace](by-namespace.md)).

## See also

- [ByNamespace](by-namespace.md) — namespace-based convention
- [ByTypeName](by-type-name.md) — namespace + type-name-based convention
- [DecoratorFor&lt;T&gt;](../byconfiguration/decorator-for.md)
- [Lifetime](../byconfiguration/lifetime.md)
