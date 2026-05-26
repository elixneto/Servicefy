---
title: AddScoped
---

# AddScoped

[← Back to ByConfiguration](index.md)

Registers the decorated class with a **Scoped** lifetime
(`services.AddScoped<TService, TImpl>()`).

## Signatures

```csharp
[AddScoped]
public class UserService : IUserService { }

[AddScoped(typeof(IUserService))]
public class UserService : IUserService, IUserNotifier { }

[AddScoped<IUserService>]
public class UserService : IUserService, IUserNotifier { }
```

## Overloads

### `AddScoped()`

Registers against **every directly implemented interface** — one registration per interface, all
**Scoped**.

```csharp
[AddScoped]
public class UserService : IUserService, IUserNotifier { }
```
```csharp
// generated
services.AddScoped<IUserService, UserService>();
services.AddScoped<IUserNotifier, UserService>();
```

**Diagnostics:** [`SVCFY003`](../diagnostics.md#svcfy003) if the class implements no interfaces.

### `AddScoped(Type serviceType)`

Registers explicitly as `serviceType` only.

| Parameter | Description |
|-----------|-------------|
| `serviceType` | An interface or base type implemented by the decorated class. |

```csharp
[AddScoped(typeof(IUserService))]
public class UserService : IUserService, IUserNotifier { }
```
```csharp
// generated — only IUserService, IUserNotifier is NOT registered
services.AddScoped<IUserService, UserService>();
```

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `serviceType`.

### `AddScoped<TService>`

Generic form of the explicit-type overload. Equivalent to `AddScoped(typeof(TService))`.

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `TService`.

## Decorators

If the registered interface has decorators ([`[DecoratorFor<T>]`](decorator-for.md) and/or
`.Decorate<,>()`), the generator emits the full decorator chain instead of a plain
`AddScoped<TService, TImpl>()` call.

## See also

- [AddSingleton](add-singleton.md), [AddTransient](add-transient.md) — other fixed lifetimes
- [Add](add.md) — generic form with an explicit [`Lifetime`](lifetime.md)
- [AddKeyedScoped](add-keyed-scoped.md) — keyed variant
- [Conventions → ByNamespace](../conventions/by-namespace.md) — namespace-based equivalent
