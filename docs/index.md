---
title: Servicefy
---

# Servicefy

**Servicefy** is a Roslyn source generator that wires up dependency injection registrations for
[`Microsoft.Extensions.DependencyInjection`](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection)
at **compile time**. There is no runtime reflection and the generated code is fully **AOT-safe**.

```bash
dotnet add package Servicefy
```

Two independent, composable approaches are available — use either one alone, or both together in the
same project.

## [ByConfiguration](byconfiguration/index.md)

Explicit, opt-in registration via attributes applied directly to your classes and interfaces:

```csharp
[AddScoped]
public class UserService : IUserService { }

public interface IUserService { }

[DecoratorFor<IUserService>]
public class LoggingDecorator : IUserService
{
    public LoggingDecorator(IUserService inner) { }
}
```

[Browse all attributes →](byconfiguration/index.md)

## [Conventions](conventions/index.md)

Convention-based, zero-attribute scanning fully resolved at compile time:

```csharp
services.AddServicefyConventions()
    .ByNamespace(ns => ns.StartsWith("MyApp.Services"), Lifetime.Scoped)
    .ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)
    .ByNamespaceOf<SomeTypeInMyApp.Data.Marker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped)
    .ByTypeName((ns, name) => name.EndsWith("Repository") && ns.Equals("MyApp.Implementations"), Lifetime.Scoped);
```

[Browse conventions →](conventions/index.md)

## Decorators

`[DecoratorFor<TService>]`, `[Decorator]` and `.Decorate<TService, TDecorator>()` work identically for
both approaches — a decorated interface generates the same `AddKeyedXxx("__BASE__")` chain whether it's
registered via `[AddScoped]` or via `.ByNamespace(...)` / `.ByBaseType(...)`. See
[DecoratorFor](byconfiguration/decorator-for.md) and [Decorator](byconfiguration/decorator.md).

## Diagnostics

All compile-time errors and validation rules are listed on the [Diagnostics](diagnostics.md) page
(`SVCFY001`–`SVCFY014`).

## Troubleshooting

Having trouble with your IDE not resolving generated code? See
[Troubleshooting](troubleshooting.md).
