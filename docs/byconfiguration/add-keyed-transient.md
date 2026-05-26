---
title: AddKeyedTransient
---

# AddKeyedTransient

[← Back to ByConfiguration](index.md)

Registers the decorated class as a **keyed Transient** service
(`services.AddKeyedTransient<TService, TImpl>(serviceKey)`).

## Signatures

```csharp
[AddKeyedTransient("welcome")]
public class WelcomeEmailTemplate : IEmailTemplate { }

[AddKeyedTransient("welcome", typeof(IEmailTemplate))]
public class WelcomeEmailTemplate : IEmailTemplate, IValidatable { }

[AddKeyedTransient<IEmailTemplate>("welcome")]
public class WelcomeEmailTemplate : IEmailTemplate, IValidatable { }
```

## Overloads

### `AddKeyedTransient(object serviceKey)`

Registers against **every directly implemented interface** — one registration per interface, all
keyed with `serviceKey`, all **Transient**.

| Parameter | Description |
|-----------|-------------|
| `serviceKey` | The key used with `GetRequiredKeyedService<T>(key)` / `GetKeyedService<T>(key)`. |

```csharp
[AddKeyedTransient("welcome")]
public class WelcomeEmailTemplate : IEmailTemplate, IValidatable { }
```
```csharp
// generated
services.AddKeyedTransient<IEmailTemplate, WelcomeEmailTemplate>("welcome");
services.AddKeyedTransient<IValidatable, WelcomeEmailTemplate>("welcome");
```

**Diagnostics:** [`SVCFY003`](../diagnostics.md#svcfy003) if the class implements no interfaces.

### `AddKeyedTransient(object serviceKey, Type serviceType)`

Registers explicitly as `serviceType` only, keyed with `serviceKey`.

| Parameter | Description |
|-----------|-------------|
| `serviceKey` | The key used with `GetRequiredKeyedService<T>(key)` / `GetKeyedService<T>(key)`. |
| `serviceType` | An interface or base type implemented by the decorated class. |

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `serviceType`.

### `AddKeyedTransient<TService>(object serviceKey)`

Generic form of the explicit-type overload. Equivalent to `AddKeyedTransient(serviceKey, typeof(TService))`.

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `TService`.

## Note on `[DecoratorFor<T>]`

Decorator classes (targets of [`[DecoratorFor<T>]`](decorator-for.md) or `.Decorate<,>()`) **must** be registered with `[AddKeyed*]`
or no attribute at all — a non-keyed `[Add*]` on a decorator triggers
[`SVCFY008`](../diagnostics.md#svcfy008), since the decorator chain itself relies on keyed registrations
internally (`"__BASE__"` / `"<TypeName>"`).

## See also

- [AddKeyedScoped](add-keyed-scoped.md), [AddKeyedSingleton](add-keyed-singleton.md) — other keyed lifetimes
- [AddTransient](add-transient.md) — non-keyed equivalent
