---
title: ByConfiguration
---

# ByConfiguration

[← Back to Servicefy](../index.md)

Explicit, opt-in dependency injection registration via attributes generated into your assembly. No
runtime reflection — everything is resolved by the source generator at compile time.

```csharp
[AddScoped]
public class UserService : IUserService { }
```

## Attributes

| Attribute | Target | Description |
|-----------|--------|-------------|
| [`Add`](add.md) | Class | Generic form with an explicit [`Lifetime`](lifetime.md) |
| [`AddScoped`](add-scoped.md) | Class | Registers with a **Scoped** lifetime |
| [`AddTransient`](add-transient.md) | Class | Registers with a **Transient** lifetime |
| [`AddSingleton`](add-singleton.md) | Class | Registers with a **Singleton** lifetime |
| [`AddKeyedScoped`](add-keyed-scoped.md) | Class | Keyed registration with a **Scoped** lifetime |
| [`AddKeyedTransient`](add-keyed-transient.md) | Class | Keyed registration with a **Transient** lifetime |
| [`AddKeyedSingleton`](add-keyed-singleton.md) | Class | Keyed registration with a **Singleton** lifetime |
| [`AddSelf`](add-self.md) | Class | Self-registration with an explicit [`Lifetime`](lifetime.md), ignoring interfaces |
| [`AddSelfScoped`](add-self-scoped.md) | Class | Self-registration with a **Scoped** lifetime |
| [`AddSelfTransient`](add-self-transient.md) | Class | Self-registration with a **Transient** lifetime |
| [`AddSelfSingleton`](add-self-singleton.md) | Class | Self-registration with a **Singleton** lifetime |
| [`Configure`](configure.md) | Class | Binds a configuration section to an options class |
| [`Decorator`](decorator.md) | Class | Marks a decorator class for the single interface it implements (`TService` inferred) |
| [`DecoratorFor<TService>`](decorator-for.md) | Class | Marks a decorator class for `TService` |
| [`.Decorate<TService, TDecorator>()`](decorate-extension.md) | Extension | Adds an outer decorator layer for `TService` |

## Shared types

| Type | Description |
|------|-------------|
| [`Lifetime`](lifetime.md) | `Singleton` \| `Scoped` \| `Transient` — used by every attribute above and by [Conventions](../conventions/index.md) |

## Multi-interface registration

A no-explicit-type attribute (`[AddScoped]`, `[AddKeyedScoped("key")]`, …) applied to a class that
implements **two or more interfaces** registers the class against **all** of them — one registration
per interface, with the same lifetime/key.

```csharp
[AddScoped]
public class UserService : IUserService, IUserNotifier { }

// generates:
services.AddScoped<IUserService, UserService>();
services.AddScoped<IUserNotifier, UserService>();
```

## Diagnostics

See [Diagnostics](../diagnostics.md) for the full list of compile-time checks.
