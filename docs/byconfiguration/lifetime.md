---
title: Lifetime
---

# Lifetime

[← Back to ByConfiguration](index.md)

Enum used by every Servicefy registration attribute and by [Conventions](../conventions/index.md) to
specify the lifetime passed to `Microsoft.Extensions.DependencyInjection.IServiceCollection`.

## Definition

```csharp
internal enum Lifetime
{
    Singleton = 0,
    Scoped,
    Transient
}
```

| Member | Maps to | Description |
|--------|---------|-------------|
| `Singleton` | `AddSingleton` | One instance for the lifetime of the application. |
| `Scoped` | `AddScoped` | One instance per request/scope. |
| `Transient` | `AddTransient` | A new instance every time it is requested. |

## Used by

- [Add](add.md), [AddSelf](add-self.md)
- [Configure](configure.md)
- [Conventions → ByNamespace](../conventions/by-namespace.md)
- [Conventions → ByBaseType](../conventions/by-base-type.md)

> `AddScoped`/`AddSingleton`/`AddTransient` and their `AddKeyed*`/`AddSelf*` counterparts encode the
> lifetime in the attribute name and don't take a `Lifetime` argument.
