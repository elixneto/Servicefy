---
title: DecoratorFor<T>
---

# DecoratorFor&lt;T&gt;

[← Back to ByConfiguration](index.md)

Marks a **decorator class** as a decorator for the service interface `TService`. Combined with the
[`.Decorate<TService, TDecorator>()`](decorate-extension.md) extension, decorators are merged
into a single chain for `TService` — the **outermost → innermost** order is:

```
[.Decorate<,>() calls, in declaration order] ++ [DecoratorFor<T> classes, sorted by fully-qualified name]
```

`.Decorate<,>()` calls add **outer** layers — the first call declared is the **outermost** decorator.
`[DecoratorFor<T>]` classes have no relative order between themselves and always form the inner/base
layers, sorted deterministically (but unspecified) by fully-qualified type name.

`[DecoratorFor<T>]` / `.Decorate<,>()` are shared between both registration approaches: a decorated
interface gets the exact same generated chain whether it's registered via `[AddScoped]` /
`[AddSingleton]` / `[AddTransient]` ([ByConfiguration](index.md)) or via `.ByNamespace(...)` /
`.ByBaseType(...)` ([Conventions](../conventions/index.md)).

> If the decorator implements **exactly one** interface, [`[Decorator]`](decorator.md) can be used
> instead — it infers `TService` from that interface, so you don't have to repeat it.

## Signature

```csharp
public interface IUserService { }

[AddScoped]
public class UserService : IUserService { }

[DecoratorFor<IUserService>]   // inner/base layer (FQN-sorted)
public class UmDecorator : IUserService
{
    public UmDecorator(IUserService inner) { }
}

public class LoggingDecorator : IUserService { /* declared first -> outermost */ }
public class CacheDecorator : IUserService    { /* declared second */ }
```

```csharp
services.Decorate<IUserService, LoggingDecorator>();
services.Decorate<IUserService, CacheDecorator>();
```

| Type parameter | Description |
|-----------------|-------------|
| `TService` | The decorated interface. Each decorator must have a public constructor accepting `TService`. Extra constructor parameters are resolved via `GetRequiredService<T>()`, or `GetRequiredKeyedService<T>(key)` when annotated with `[FromKeyedServices(key)]`. |

## Pure fluent example — order with two `.Decorate<,>()` calls

With only `.Decorate<,>()` calls (no `[DecoratorFor<T>]` / `[Decorator]`), the **first** call declared
is still the **outermost** layer — even though it ends up as the **last** keyed registration in the
generated chain:

```csharp
public interface IUserService { }

[AddScoped]
public class UserService : IUserService { }

public class CacheDecorator : IUserService
{
    public CacheDecorator(IUserService inner) { }
}

public class LoggingDecorator : IUserService
{
    public LoggingDecorator(IUserService inner) { }
}
```

```csharp
services.Decorate<IUserService, CacheDecorator>()    // declared first -> outermost
    .Decorate<IUserService, LoggingDecorator>();     // declared last  -> innermost
```

Generated chain:

```csharp
services.AddKeyedScoped<IUserService, UserService>("__BASE__");
services.AddKeyedScoped<IUserService>("TestAssembly.LoggingDecorator",
    (sp, _) => new LoggingDecorator(sp.GetRequiredKeyedService<IUserService>("__BASE__")));
services.AddKeyedScoped<IUserService>("TestAssembly.CacheDecorator",
    (sp, _) => new CacheDecorator(sp.GetRequiredKeyedService<IUserService>("TestAssembly.LoggingDecorator")));
services.AddScoped<IUserService>(sp =>
    sp.GetRequiredKeyedService<IUserService>("TestAssembly.CacheDecorator"));
```

`LoggingDecorator` (declared last) wraps `UserService` directly — it's the **innermost** layer.
`CacheDecorator` (declared first) wraps `LoggingDecorator` and is what the final non-keyed resolver
points at — it's the **outermost** layer, so it runs first (and last) when `IUserService.SomeMethod()`
is called:

```
CacheDecorator   -> before
LoggingDecorator -> before
UserService      -> SomeMethod()
LoggingDecorator -> after
CacheDecorator   -> after
```

## Constructor selection

For each decorator type, the generator picks the constructor in this order:

1. The constructor annotated with `[ActivatorUtilitiesConstructor]`, if any.
2. The single public constructor, if there is exactly one.
3. Otherwise, the constructor with the most parameters.

## Diagnostics

- [`SVCFY007`](../diagnostics.md#svcfy007) — the decorator has no public constructor with a parameter of
  the decorated interface type.
- [`SVCFY008`](../diagnostics.md#svcfy008) — the decorator class is itself registered with a non-keyed
  `[Add*]` attribute (it must use `[AddKeyed*]` or no registration attribute at all).
- [`SVCFY013`](../diagnostics.md#svcfy013) — the decorator does not implement `TService`.

## Notes

- A `[DecoratorFor<T>]` class or `.Decorate<,>()` target that is **never registered** by either generator
  produces no diagnostic — decorators only apply when the underlying service is actually registered.

## See also

- [Decorator](decorator.md)
- [.Decorate<TService, TDecorator>()](decorate-extension.md)
- [AddScoped](add-scoped.md), [AddSingleton](add-singleton.md), [AddTransient](add-transient.md)
- [Conventions → ByNamespace](../conventions/by-namespace.md)
- [Conventions → ByBaseType](../conventions/by-base-type.md)
