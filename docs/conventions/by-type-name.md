---
title: ByTypeName
---

# ByTypeName

[← Back to Conventions](index.md)

Registers every concrete, non-abstract, non-generic class whose **namespace and type name** satisfy a
two-parameter predicate, against each of its **directly implemented interfaces** (one registration per
interface), using the given [`Lifetime`](../byconfiguration/lifetime.md).

```csharp
services.AddServicefyConventions()
    .ByTypeName((ns, name) => name.EndsWith("Repository") && ns.Equals("MyApp.Implementations"), Lifetime.Scoped);
```

## Signature

```csharp
IServicefyConventionsBuilder ByTypeName(
    Func<string, string, bool> predicate,
    Lifetime lifetime,
    [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");
```

| Parameter | Description |
|-----------|-------------|
| `predicate` | A two-parameter lambda with an expression body, `(ns, name) => ...` — see [Supported predicates](#supported-predicates) below. |
| `lifetime` | The [`Lifetime`](../byconfiguration/lifetime.md) used for every matched registration. |

**Returns:** the same builder, for chaining additional convention calls.

## What gets registered

For each matching type:

- Classes with **zero** implemented interfaces are **skipped**.
- Classes with **2+** interfaces are registered against **all** of them — one `Add{Lifetime}<IFoo, TImpl>()`
  per interface, same lifetime.
- Abstract/static classes and **open generics** are skipped.

```csharp
namespace MyApp.Implementations
{
    public interface IUserRepository { }
    public class UserRepository : IUserRepository { }
}
```
```csharp
// generated (lifetime == Scoped, predicate == (ns, name) => name.EndsWith("Repository") && ns.Equals("MyApp.Implementations"))
services.AddScoped<IUserRepository, UserRepository>();
```

## Supported predicates

`predicate` must be a **two-parameter lambda with an expression body**, `(ns, name) => ...`, where
`ns` is the candidate's full namespace and `name` is its simple type name. Each leaf expression is
built from:

- `StartsWith` / `EndsWith` / `Contains` / `Equals` on either parameter — optionally with a `StringComparison` argument
- `StartsWith` / `EndsWith` / `Contains` on either parameter with **multiple string arguments** (params
  form) or an **array/collection literal** of string literals — matches if **any** value matches,
  optionally followed by a `StringComparison` argument
- `==` / `!=` against string literals, on either parameter
- The literal `true` — matches every type (`false` is not supported, since it would match nothing)
- `!` (negation)
- `&&` / `||`
- Parentheses for grouping

```csharp
.ByTypeName((ns, name) => name.EndsWith("Repository") && ns.Equals("MyApp.Implementations"), Lifetime.Scoped)
.ByTypeName((ns, name) => name.StartsWith("Foo") || name.StartsWith("Bar"), Lifetime.Singleton)
.ByTypeName((ns, name) => ns.StartsWith("MyApp.Handlers") && !name.EndsWith("Base"), Lifetime.Transient)
.ByTypeName((ns, name) => name.EndsWith("Repository", "Handler"), Lifetime.Scoped)
.ByTypeName((ns, name) => name.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)
.ByTypeName((_, _) => true, Lifetime.Transient)
```

Any other shape **fails to parse** and the call site is silently ignored — no registration is generated
for it, and [`SVCFY012`](../diagnostics.md#svcfy012) is reported. If a `StartsWith`/`EndsWith`/`Contains`
array/params argument contains a non-literal element, the call site is ignored and
[`SVCFY011`](../diagnostics.md#svcfy011) is reported instead.

## Decorators

If a matched type's interface has decorators ([`[DecoratorFor<T>]`](../byconfiguration/decorator-for.md)
and/or `.Decorate<,>()`), the full decorator chain is emitted instead of a plain
`Add{Lifetime}<IFoo, TImpl>()`. Classes used as `[DecoratorFor<T>]` / `.Decorate<,>()`
targets are excluded from matching, so they aren't accidentally registered as plain services.
[`SVCFY008`](../diagnostics.md#svcfy008) is validated once across the union of all matched interfaces
from all rules, to avoid duplicate diagnostics.

## Interop with ByConfiguration

Types already annotated with a [ByConfiguration](../byconfiguration/index.md) attribute (`[Add*]`,
`[Configure]`) are excluded from `ByTypeName` matching.

## How it works (dispatch)

`ByTypeName` is a thin wrapper that captures the predicate's source text via `CallerArgumentExpression`
and calls a generator-implemented partial method, `ApplyByTypeName(lifetime, predicateExpression)`. The
generator emits one `if (lifetime == ... && predicateExpression == "...") { ...; return; }` branch per
distinct call site found in the compilation. If a rule matches zero types, no branch is emitted for it —
and if **no** rule matches anything, no file is emitted at all (same as
[ByNamespace](by-namespace.md)).

## See also

- [ByNamespace](by-namespace.md) — namespace-based convention
- [ByNamespaceOf](by-namespace-of.md) — namespace convention scoped to a marker type
- [DecoratorFor&lt;T&gt;](../byconfiguration/decorator-for.md)
- [Lifetime](../byconfiguration/lifetime.md)
