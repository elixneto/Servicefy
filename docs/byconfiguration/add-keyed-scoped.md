---
title: AddKeyedScoped
---

# AddKeyedScoped

[← Back to ByConfiguration](index.md)

Registers the decorated class as a **keyed Scoped** service
(`services.AddKeyedScoped<TService, TImpl>(serviceKey)`).

## Signatures

```csharp
[AddKeyedScoped("primary")]
public class PrimaryPaymentGateway : IPaymentGateway { }

[AddKeyedScoped("primary", typeof(IPaymentGateway))]
public class PrimaryPaymentGateway : IPaymentGateway, IHealthCheckable { }

[AddKeyedScoped<IPaymentGateway>("primary")]
public class PrimaryPaymentGateway : IPaymentGateway, IHealthCheckable { }
```

## Overloads

### `AddKeyedScoped(object serviceKey)`

Registers against **every directly implemented interface** — one registration per interface, all
keyed with `serviceKey`, all **Scoped**.

| Parameter | Description |
|-----------|-------------|
| `serviceKey` | The key used with `GetRequiredKeyedService<T>(key)` / `GetKeyedService<T>(key)`. |

```csharp
[AddKeyedScoped("primary")]
public class PrimaryPaymentGateway : IPaymentGateway, IHealthCheckable { }
```
```csharp
// generated
services.AddKeyedScoped<IPaymentGateway, PrimaryPaymentGateway>("primary");
services.AddKeyedScoped<IHealthCheckable, PrimaryPaymentGateway>("primary");
```

**Diagnostics:** [`SVCFY003`](../diagnostics.md#svcfy003) if the class implements no interfaces.

### `AddKeyedScoped(object serviceKey, Type serviceType)`

Registers explicitly as `serviceType` only, keyed with `serviceKey`.

| Parameter | Description |
|-----------|-------------|
| `serviceKey` | The key used with `GetRequiredKeyedService<T>(key)` / `GetKeyedService<T>(key)`. |
| `serviceType` | An interface or base type implemented by the decorated class. |

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `serviceType`.

### `AddKeyedScoped<TService>(object serviceKey)`

Generic form of the explicit-type overload. Equivalent to `AddKeyedScoped(serviceKey, typeof(TService))`.

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `TService`.

## Note on `[DecoratorFor<T>]`

Decorator classes (targets of [`[DecoratorFor<T>]`](decorator-for.md) or `.Decorate<,>()`) **must** be registered with `[AddKeyed*]`
or no attribute at all — a non-keyed `[Add*]` on a decorator triggers
[`SVCFY008`](../diagnostics.md#svcfy008), since the decorator chain itself relies on keyed registrations
internally (`"__BASE__"` / `"<TypeName>"`).

## See also

- [AddKeyedSingleton](add-keyed-singleton.md), [AddKeyedTransient](add-keyed-transient.md) — other keyed lifetimes
- [AddScoped](add-scoped.md) — non-keyed equivalent
