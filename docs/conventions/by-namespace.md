---
title: ByNamespace
---

# ByNamespace

[← Back to Conventions](index.md)

Registers every concrete, non-abstract, non-generic class whose **namespace** matches a predicate
against each of its **directly implemented interfaces** (one registration per interface), using the
given [`Lifetime`](../byconfiguration/lifetime.md).

```csharp
services.AddServicefyConventions()
    .ByNamespace(ns => ns.StartsWith("MyApp.Services"), Lifetime.Scoped);
```

## Signature

```csharp
IServicefyConventionsBuilder ByNamespace(
    Func<string, bool> predicate,
    Lifetime lifetime,
    ConventionScope scope = ConventionScope.Compilation,
    [CallerArgumentExpression(nameof(predicate))] string predicateExpression = "");
```

| Parameter | Description |
|-----------|-------------|
| `predicate` | A single-parameter lambda with an expression body — see [Supported predicates](#supported-predicates) below. |
| `lifetime` | The [`Lifetime`](../byconfiguration/lifetime.md) used for every matched registration. |
| `scope` | Optional. Where candidates come from: `ConventionScope.Compilation` (default) or `ConventionScope.AllReferences` — see [Scope](#scope). Must be written as `ConventionScope.X`. |

**Returns:** the same builder, for chaining additional convention calls.

## What gets registered

For each matching type:

- Classes with **zero** implemented interfaces are **skipped**.
- Classes with **2+** interfaces are registered against **all** of them — one `Add{Lifetime}<IFoo, TImpl>()`
  per interface, same lifetime.
- Abstract/static classes and **open generics** are skipped.

```csharp
namespace MyApp.Services
{
    public class UserService : IUserService, IUserNotifier { }
}
```
```csharp
// generated (lifetime == Scoped, predicate == nm.StartsWith("MyApp.Services"))
services.AddScoped<IUserService, UserService>();
services.AddScoped<IUserNotifier, UserService>();
```

## Supported predicates

`predicate` must be a **single-parameter lambda with an expression body**, built from:

- `StartsWith` / `EndsWith` / `Contains` / `Equals` — optionally with a `StringComparison` argument
- `StartsWith` / `EndsWith` / `Contains` with **multiple string arguments** (params form) or an
  **array/collection literal** of string literals — matches if **any** value matches. A
  `StringComparison` can precede the values (`EndsWith(StringComparison.OrdinalIgnoreCase, "Repository", "Handler")`)
  or follow the array (`EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase)`)
- `==` / `!=` against string literals
- The literal `true` — matches every type (`false` is not supported, since it would match nothing)
- `!` (negation)
- `&&` / `||`
- Parentheses for grouping

```csharp
.ByNamespace(ns => ns.StartsWith("MyApp.Services"), Lifetime.Scoped)
.ByNamespace(ns => ns.EndsWith(".Infrastructure") && !ns.Contains("Tests"), Lifetime.Singleton)
.ByNamespace(ns => ns == "MyApp.Handlers", Lifetime.Transient)
.ByNamespace(ns => ns.EndsWith("Repository", "Handler"), Lifetime.Scoped)
.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)
.ByNamespace(_ => true, Lifetime.Transient)
```

Any other shape **fails to parse** and the call site is silently ignored — no registration is generated
for it, and [`SVCFY012`](../diagnostics.md#svcfy012) is reported. If a `StartsWith`/`EndsWith`/`Contains`
array/params argument contains a non-literal element, the call site is ignored and
[`SVCFY011`](../diagnostics.md#svcfy011) is reported instead.

## Scope

By default (`ConventionScope.Compilation`) candidates come from the project being compiled and its
`<ProjectReference>`s. NuGet packages are never scanned.

`ConventionScope.AllReferences` opts one call into also scanning **every referenced NuGet package,
transitive dependencies included** (the .NET SDK's own assemblies, and targeting packs restored into
the NuGet folder, never are). For that call, public classes from packages become candidates, and
interfaces declared in packages become registrable services, so a class in your project implementing
a package interface is registered against it too.

```csharp
// NuGet package "MyCompany.Shared" (maybe only a transitive dependency):
namespace MyCompany.Shared.Clock { public interface IClock { } public class SystemClock : IClock { } }

services.AddServicefyConventions()
    .ByNamespace(ns => ns.StartsWith("MyCompany.Shared"), Lifetime.Singleton, ConventionScope.AllReferences);

// generates:
services.AddSingleton<MyCompany.Shared.Clock.IClock, MyCompany.Shared.Clock.SystemClock>();
```

Without `ConventionScope.AllReferences` the same call registers nothing from the package.

With `AllReferences`, a predicate bounded by `StartsWith` lets the generator **prune** the package
walk: for `ns => ns.StartsWith("MyCompany.Shared")` only the `MyCompany` → `MyCompany.Shared*`
namespaces of each package are walked, nothing else. Pruning applies when every namespace the
predicate can match is guaranteed to start with a literal prefix: `StartsWith` (any of its forms,
including several values and a `StringComparison`), `==` / `Equals` with a literal, `&&` with such a
side, and `||` where both sides are such and use the same `StringComparison`. `EndsWith`, `Contains`, `!` and `_ => true` cannot be pruned:
every package is walked.

Things to be aware of:

- **Noise.** Packages ship many public classes. A broad predicate registers types from libraries you
  never meant to touch. Keep the predicate specific to your own namespaces. `_ => true` and very short `StartsWith` prefixes (under 4 characters) are reported as warning
  [`SVCFY017`](../diagnostics.md#svcfy017). The call is still applied.
- **Cost.** Each package is walked once per build/IDE session and cached (per assembly, or per namespace
  when pruned), so the cost is paid on the first run, not on every edit. In the
  [benchmark](https://github.com/elixneto/Servicefy/issues/9) (54 NuGet assemblies), two `AllReferences`
  rules added about 6 ms per edit.
- Packages are detected by their path under the NuGet global packages folder (`.nuget/packages`).
  Interfaces from the BCL (e.g. `IDisposable`) are still never registered.

## Decorators

If a matched type's interface has decorators ([`[DecoratorFor<T>]`](../byconfiguration/decorator-for.md)
and/or `.Decorate<,>()`), the full decorator chain is emitted instead of a plain
`Add{Lifetime}<IFoo, TImpl>()`. Classes used as `[DecoratorFor<T>]` / `.Decorate<,>()`
targets are excluded from matching, so they aren't accidentally registered as plain services.
[`SVCFY008`](../diagnostics.md#svcfy008) is validated once across the union of all matched interfaces
from all `ByNamespace`/`ByBaseType` rules, to avoid duplicate diagnostics.

## Interop with ByConfiguration

Types already annotated with a [ByConfiguration](../byconfiguration/index.md) attribute (`[Add*]`,
`[Configure]`) are excluded from `ByNamespace` matching.

## How it works (dispatch)

`ByNamespace` is a thin wrapper that captures the predicate's source text via `CallerArgumentExpression`
and calls a generator-implemented partial method, `ApplyByNamespace(lifetime, scope, predicateExpression)`. The
generator emits one `if (lifetime == ... && scope == ... && predicateExpression == "...") { ...; return; }` branch per
distinct call site found in the compilation. If a rule matches zero types, no branch is emitted for it —
and if **no** rule matches anything, no file is emitted at all.

## See also

- [ByBaseType](by-base-type.md) — type-hierarchy-based convention
- [ByNamespaceOf](by-namespace-of.md) — namespace convention scoped to a marker type
- [ByTypeName](by-type-name.md) — namespace + type-name-based convention
- [DecoratorFor&lt;T&gt;](../byconfiguration/decorator-for.md)
- [Lifetime](../byconfiguration/lifetime.md)
