---
title: AddSingleton
---

# AddSingleton

[← Back to ByConfiguration](index.md)

Registers the decorated class with a **Singleton** lifetime
(`services.AddSingleton<TService, TImpl>()`).

## Signatures

```csharp
[AddSingleton]
public class CacheService : ICacheService { }

[AddSingleton(typeof(ICacheService))]
public class CacheService : ICacheService, IDisposableResource { }

[AddSingleton<ICacheService>]
public class CacheService : ICacheService, IDisposableResource { }
```

## Overloads

### `AddSingleton()`

Registers against **every directly implemented interface** — one registration per interface, all
**Singleton**.

```csharp
[AddSingleton]
public class CacheService : ICacheService, IDisposableResource { }
```
```csharp
// generated
services.AddSingleton<ICacheService, CacheService>();
services.AddSingleton<IDisposableResource, CacheService>();
```

**Diagnostics:** [`SVCFY003`](../diagnostics.md#svcfy003) if the class implements no interfaces.

### `AddSingleton(Type serviceType)`

Registers explicitly as `serviceType` only.

| Parameter | Description |
|-----------|-------------|
| `serviceType` | An interface or base type implemented by the decorated class. |

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `serviceType`.

### `AddSingleton<TService>`

Generic form of the explicit-type overload. Equivalent to `AddSingleton(typeof(TService))`.

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `TService`.

## Decorators

If the registered interface has decorators ([`[DecoratorFor<T>]`](decorator-for.md) and/or
`.Decorate<,>()`), the generator emits the full decorator chain instead of a plain
`AddSingleton<TService, TImpl>()` call.

## See also

- [AddScoped](add-scoped.md), [AddTransient](add-transient.md) — other fixed lifetimes
- [Add](add.md) — generic form with an explicit [`Lifetime`](lifetime.md)
- [AddKeyedSingleton](add-keyed-singleton.md) — keyed variant
