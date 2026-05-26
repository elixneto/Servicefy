---
title: Add
---

# Add

[← Back to ByConfiguration](index.md)

Generic registration attribute with an explicit [`Lifetime`](lifetime.md). Equivalent to
[`AddScoped`](add-scoped.md) / [`AddSingleton`](add-singleton.md) / [`AddTransient`](add-transient.md),
but the lifetime is a runtime value passed to the attribute instead of being baked into the attribute
name.

## Signatures

```csharp
[Add(Lifetime.Scoped)]
public class UserService : IUserService { }

[Add(Lifetime.Scoped, typeof(IUserService))]
public class UserService : IUserService, IUserNotifier { }

[Add<IUserService>(Lifetime.Scoped)]
public class UserService : IUserService, IUserNotifier { }
```

## Overloads

### `Add(Lifetime lifetime)`

Registers against **every directly implemented interface** — one registration per interface, using
`lifetime`.

| Parameter | Description |
|-----------|-------------|
| `lifetime` | The desired registration [`Lifetime`](lifetime.md). |

**Diagnostics:**
[`SVCFY001`](../diagnostics.md#svcfy001) if `lifetime` is omitted ·
[`SVCFY003`](../diagnostics.md#svcfy003) if the class implements no interfaces.

### `Add(Lifetime lifetime, Type serviceType)`

Registers explicitly as `serviceType` only, using `lifetime`.

| Parameter | Description |
|-----------|-------------|
| `lifetime` | The desired registration [`Lifetime`](lifetime.md). |
| `serviceType` | An interface or base type implemented by the decorated class. |

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `serviceType`.

### `Add<TService>(Lifetime lifetime)`

Generic form of the explicit-type overload. Equivalent to `Add(lifetime, typeof(TService))`.

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `TService`.

## Decorators

If the registered interface has decorators ([`[DecoratorFor<T>]`](decorator-for.md) and/or
`.Decorate<,>()`), the generator emits the full decorator chain instead of a plain
`Add{Lifetime}<TService, TImpl>()` call.

## See also

- [AddScoped](add-scoped.md), [AddSingleton](add-singleton.md), [AddTransient](add-transient.md) — fixed-lifetime shorthands
- [AddSelf](add-self.md) — self-registration with an explicit `Lifetime`
- [Lifetime](lifetime.md)
