---
title: AddKeyedSingleton
---

# AddKeyedSingleton

[← Back to ByConfiguration](index.md)

Registers the decorated class as a **keyed Singleton** service
(`services.AddKeyedSingleton<TService, TImpl>(serviceKey)`).

## Signatures

```csharp
[AddKeyedSingleton("redis")]
public class RedisCache : ICache { }

[AddKeyedSingleton("redis", typeof(ICache))]
public class RedisCache : ICache, IHealthCheckable { }

[AddKeyedSingleton<ICache>("redis")]
public class RedisCache : ICache, IHealthCheckable { }
```

## Overloads

### `AddKeyedSingleton(object serviceKey)`

Registers against **every directly implemented interface** — one registration per interface, all
keyed with `serviceKey`, all **Singleton**.

| Parameter | Description |
|-----------|-------------|
| `serviceKey` | The key used with `GetRequiredKeyedService<T>(key)` / `GetKeyedService<T>(key)`. |

```csharp
[AddKeyedSingleton("redis")]
public class RedisCache : ICache, IHealthCheckable { }
```
```csharp
// generated
services.AddKeyedSingleton<ICache, RedisCache>("redis");
services.AddKeyedSingleton<IHealthCheckable, RedisCache>("redis");
```

**Diagnostics:** [`SVCFY003`](../diagnostics.md#svcfy003) if the class implements no interfaces.

### `AddKeyedSingleton(object serviceKey, Type serviceType)`

Registers explicitly as `serviceType` only, keyed with `serviceKey`.

| Parameter | Description |
|-----------|-------------|
| `serviceKey` | The key used with `GetRequiredKeyedService<T>(key)` / `GetKeyedService<T>(key)`. |
| `serviceType` | An interface or base type implemented by the decorated class. |

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `serviceType`.

### `AddKeyedSingleton<TService>(object serviceKey)`

Generic form of the explicit-type overload. Equivalent to `AddKeyedSingleton(serviceKey, typeof(TService))`.

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `TService`.

## Note on `[DecoratorFor<T>]`

Decorator classes (targets of [`[DecoratorFor<T>]`](decorator-for.md) or `.Decorate<,>()`) **must** be registered with `[AddKeyed*]`
or no attribute at all — a non-keyed `[Add*]` on a decorator triggers
[`SVCFY008`](../diagnostics.md#svcfy008), since the decorator chain itself relies on keyed registrations
internally (`"__BASE__"` / `"<TypeName>"`).

## See also

- [AddKeyedScoped](add-keyed-scoped.md), [AddKeyedTransient](add-keyed-transient.md) — other keyed lifetimes
- [AddSingleton](add-singleton.md) — non-keyed equivalent
