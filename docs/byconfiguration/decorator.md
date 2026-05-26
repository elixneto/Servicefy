---
title: Decorator
---

# Decorator

[← Back to ByConfiguration](index.md)

Marks a **decorator class** as a decorator for the **single interface it implements** — the decorated
service type (`TService`) is **inferred** from the class's own implemented interface, instead of being
specified explicitly as with [`[DecoratorFor<TService>]`](decorator-for.md).

```csharp
public interface IUserService { }

[AddScoped]
public class UserService : IUserService { }

[Decorator]   // TService = IUserService, inferred from the single interface below
public class LoggingDecorator : IUserService
{
    public LoggingDecorator(IUserService inner) { }
}
```

`[Decorator]` classes form the **inner/base layer**, exactly like `[DecoratorFor<T>]` — they are merged
into the same chain, with no relative order between themselves, sorted deterministically (but
unspecified) by fully-qualified type name. `.Decorate<,>()` calls still add **outer** layers in
declaration order. See [DecoratorFor&lt;T&gt;](decorator-for.md#signature) for the full merge rule and
generated chain shape.

## Signature

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class DecoratorAttribute : Attribute { }
```

Unlike `[DecoratorFor<TService>]`, `[Decorator]` takes **no type argument** and can be applied **at most
once** per class — the decorated interface is always the class's single implemented interface.

## Diagnostics

- [`SVCFY007`](../diagnostics.md#svcfy007) — the decorator has no public constructor with a parameter of
  the decorated interface type.
- [`SVCFY008`](../diagnostics.md#svcfy008) — the decorator class is itself registered with a non-keyed
  `[Add*]` attribute (it must use `[AddKeyed*]` or no registration attribute at all).
- [`SVCFY014`](../diagnostics.md#svcfy014) — the class does not implement **exactly one** interface, so
  `TService` cannot be inferred. Use [`[DecoratorFor<TService>]`](decorator-for.md) instead to specify the
  target interface explicitly.

## Notes

- A `[Decorator]` class that is **never registered** (i.e. its inferred interface is never registered as a
  service by either generator) produces no diagnostic — decorators only apply when the underlying service
  is actually registered.

## See also

- [DecoratorFor<T>](decorator-for.md)
- [.Decorate<TService, TDecorator>()](decorate-extension.md)
- [AddScoped](add-scoped.md), [AddSingleton](add-singleton.md), [AddTransient](add-transient.md)
