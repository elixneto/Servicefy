---
title: AddTransient
---

# AddTransient

[← Back to ByConfiguration](index.md)

Registers the decorated class with a **Transient** lifetime
(`services.AddTransient<TService, TImpl>()`).

## Signatures

```csharp
[AddTransient]
public class EmailMessage : IEmailMessage { }

[AddTransient(typeof(IEmailMessage))]
public class EmailMessage : IEmailMessage, IValidatable { }

[AddTransient<IEmailMessage>]
public class EmailMessage : IEmailMessage, IValidatable { }
```

## Overloads

### `AddTransient()`

Registers against **every directly implemented interface** — one registration per interface, all
**Transient**.

```csharp
[AddTransient]
public class EmailMessage : IEmailMessage, IValidatable { }
```
```csharp
// generated
services.AddTransient<IEmailMessage, EmailMessage>();
services.AddTransient<IValidatable, EmailMessage>();
```

**Diagnostics:** [`SVCFY003`](../diagnostics.md#svcfy003) if the class implements no interfaces.

### `AddTransient(Type serviceType)`

Registers explicitly as `serviceType` only.

| Parameter | Description |
|-----------|-------------|
| `serviceType` | An interface or base type implemented by the decorated class. |

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `serviceType`.

### `AddTransient<TService>`

Generic form of the explicit-type overload. Equivalent to `AddTransient(typeof(TService))`.

**Diagnostics:** [`SVCFY002`](../diagnostics.md#svcfy002) if the class does not implement `TService`.

## Decorators

If the registered interface has decorators ([`[DecoratorFor<T>]`](decorator-for.md) and/or
`.Decorate<,>()`), the generator emits the full decorator chain instead of a plain
`AddTransient<TService, TImpl>()` call.

## See also

- [AddScoped](add-scoped.md), [AddSingleton](add-singleton.md) — other fixed lifetimes
- [Add](add.md) — generic form with an explicit [`Lifetime`](lifetime.md)
- [AddKeyedTransient](add-keyed-transient.md) — keyed variant
