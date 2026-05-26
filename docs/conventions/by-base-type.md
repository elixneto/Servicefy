---
title: ByBaseType
---

# ByBaseType

[← Back to Conventions](index.md)

Registers every concrete, non-abstract, non-generic class
assignable to `TBase`, using the given [`Lifetime`](../byconfiguration/lifetime.md) and
[`ServiceTypeSelector`](#servicetypeselector).

```csharp
services.AddServicefyConventions()
    .ByBaseType<IService>(Lifetime.Scoped) // default selector: BaseType

    .ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)
    
    .ByBaseType<JobBase>(Lifetime.Singleton, ServiceTypeSelector.Self)
    
    .ByBaseType<IPlugin>(Lifetime.Singleton, ServiceTypeSelector.SelfWithInterfaces)
    
    .ByBaseType<IHandler>(Lifetime.Scoped, matchAttribute: typeof(HandlerAttribute));
```

## Signature

```csharp
IServicefyConventionsBuilder ByBaseType<TBase>(
    Lifetime lifetime,
    ServiceTypeSelector selector = ServiceTypeSelector.BaseType,
    Type matchAttribute = null);
```

| Parameter | Description |
|-----------|-------------|
| `TBase` | The base type or interface to scan for. See [Matching](#matching) below. |
| `lifetime` | The [`Lifetime`](../byconfiguration/lifetime.md) used for every matched registration. |
| `selector` | Controls which type(s) each matched class is registered as. Defaults to `ServiceTypeSelector.BaseType`. |
| `matchAttribute` | When set, only candidates with this attribute applied **directly** to the concrete class (no inheritance) are matched. |

**Returns:** the same builder, for chaining additional convention calls.

## Matching

- **`TBase` is an interface** — matched via `type.AllInterfaces`, which covers transitively
  implemented/inherited interfaces and closed generic interfaces (e.g. `IRepository<Order>`).
- **`TBase` is a class** — matched by walking `type`, `type.BaseType`, `type.BaseType.BaseType`, … (the
  type itself is included).
- **`matchAttribute: typeof(X)`** — only types with `X` applied as a **direct** attribute on the
  concrete class match (no inheritance).
- **Candidates** are concrete, non-abstract, non-static, non-generic classes — same exclusions as
  [ByNamespace](by-namespace.md) (infrastructure types, `[Add*]`/`[Configure]`-attributed types,
  `[DecoratorFor<T>]` / `.Decorate<,>()` targets). Unlike `ByNamespace`, classes with **zero interfaces** are still candidates
  (needed for `Self`/`BaseType` selectors with a class `TBase`).

`typeof(TBase) == typeof(X)` is an AOT-safe identity comparison and works for closed generics; open
generics are impossible by the `ByBaseType<TBase>()` syntax itself.

## ServiceTypeSelector

```csharp
internal enum ServiceTypeSelector
{
    BaseType = 0,
    ImplementedInterfaces,
    Self,
    SelfWithInterfaces
}
```

| Member | Generated registration |
|--------|------------------------|
| `BaseType` (default) | `Services.Add{Lifetime}<TBase, TImpl>()` per matched type |
| `ImplementedInterfaces` | `Services.Add{Lifetime}<IFoo, TImpl>()` for each directly implemented interface of every matched type |
| `Self` | `Services.Add{Lifetime}<TImpl>()` |
| `SelfWithInterfaces` | `Services.Add{Lifetime}<TImpl>()` **plus** `Services.Add{Lifetime}<IFoo>(sp => sp.GetRequiredService<TImpl>())` per directly implemented interface |

### Example — `BaseType`

```csharp
.ByBaseType<IService>(Lifetime.Scoped)
```
```csharp
// generated, for each type assignable to IService
services.AddScoped<IService, UserService>();
```

### Example — `ImplementedInterfaces`

```csharp
.ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)
```
```csharp
// generated, for OrderRepository : IRepository<Order>, IDisposableResource
services.AddScoped<IRepository<Order>, OrderRepository>();
services.AddScoped<IDisposableResource, OrderRepository>();
```

### Example — `Self`

```csharp
.ByBaseType<JobBase>(Lifetime.Singleton, ServiceTypeSelector.Self)
```
```csharp
// generated, for each type derived from JobBase
services.AddSingleton<SendDigestJob>();
```

`Self` registers each matched type with itself as the service type and ignores `TBase` entirely, so
`TBase` must be a **class**. Using `ServiceTypeSelector.Self` with an interface `TBase` reports
[`SVCFY010`](../diagnostics.md#svcfy010):

```csharp
// SVCFY010 — IJob is an interface and cannot be used with ServiceTypeSelector.Self
.ByBaseType<IJob>(Lifetime.Singleton, ServiceTypeSelector.Self)
```

### Example — `SelfWithInterfaces`

```csharp
.ByBaseType<IPlugin>(Lifetime.Singleton, ServiceTypeSelector.SelfWithInterfaces)
```
```csharp
// generated, for ExportPlugin : IPlugin, IConfigurable
services.AddSingleton<ExportPlugin>();
services.AddSingleton<IPlugin>(sp => sp.GetRequiredService<ExportPlugin>());
services.AddSingleton<IConfigurable>(sp => sp.GetRequiredService<ExportPlugin>());
```

### Example — `matchAttribute`

```csharp
.ByBaseType<IHandler>(Lifetime.Scoped, matchAttribute: typeof(HandlerAttribute))
```

Only classes assignable to `IHandler` **and** directly decorated with `[Handler]` are registered.

## Decorators

Same shared decorator chain as [ByNamespace](by-namespace.md), applied to the `BaseType` and
`ImplementedInterfaces` selectors, **only when exactly one matched type targets that service** — the
chain's `"__BASE__"` key is fixed and can't represent multiple implementations. If more than one matched
type would register the same (possibly decorated) interface, each gets a plain
`Services.Add{Lifetime}<IFoo, TImpl>()` and the decorator chain is skipped for that interface.
`SelfWithInterfaces` never integrates with decorators — the interface factory points at the `Self`
instance, so there's no separate "base" to decorate.

## Interop with ByConfiguration

Types already annotated with a [ByConfiguration](../byconfiguration/index.md) attribute (`[Add*]`,
`[Configure]`) are excluded from `ByBaseType` matching.

## Known limitations

- `TBase = object` matches **every** concrete class in the assembly — use with care.
- Overlapping rules (`ByNamespace` + `ByBaseType`, or two `ByBaseType` rules targeting the same type) can
  produce **duplicate registrations** — there is no cross-rule deduplication.

## How it works (dispatch)

`ByBaseType<TBase>(...)` calls a generator-implemented partial method,
`ApplyByBaseType(typeof(TBase), lifetime, selector, matchAttribute)`. The generator emits one
`if (baseType == typeof(...) && lifetime == Lifetime.X && selector == ServiceTypeSelector.Y && matchAttribute == (typeof(...) | null)) { ...; return; }`
branch per distinct call site found in the compilation. If a rule matches zero types, no branch is
emitted for it — and if **no** rule matches anything, no file is emitted at all (same as
[ByNamespace](by-namespace.md)).

## See also

- [ByNamespace](by-namespace.md) — namespace-based convention
- [ByNamespaceOf](by-namespace-of.md) — namespace convention scoped to a marker type
- [ByTypeName](by-type-name.md) — namespace + type-name-based convention
- [DecoratorFor&lt;T&gt;](../byconfiguration/decorator-for.md)
- [Lifetime](../byconfiguration/lifetime.md)
