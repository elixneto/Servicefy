---
title: Diagnostics
---

# Diagnostics
[← Back to Servicefy](./index.md)

Servicefy reports compile-time diagnostics with codes `SVCFY001`–`SVCFY016` (`SVCFY004` is
reserved/removed — see note below).

| Code | Severity | Condition |
|------|----------|-----------|
| [SVCFY001](#svcfy001) | Error | `[Add]` / `[AddSelf]` missing the required `Lifetime` argument |
| [SVCFY002](#svcfy002) | Error | Explicit service type is not implemented by the decorated class |
| [SVCFY003](#svcfy003) | Error | No-explicit-type attribute applied to a class that implements zero interfaces |
| [SVCFY005](#svcfy005) | Error | `[Configure]` missing section name or lifetime |
| [SVCFY006](#svcfy006) | Error | `[Configure]` applied to a class without a parameterless constructor |
| [SVCFY007](#svcfy007) | Error | Decorator has no public constructor accepting the decorated interface type |
| [SVCFY008](#svcfy008) | Error | Decorator class registered with a non-keyed `[Add*]` attribute |
| [SVCFY009](#svcfy009) | Error | `[AddSelf*]` applied to an abstract or static class |
| [SVCFY010](#svcfy010) | Error | `ByBaseType<TBase>(..., ServiceTypeSelector.Self)` with an interface `TBase` |
| [SVCFY011](#svcfy011) | Error | Non-literal element in a `StartsWith`/`EndsWith`/`Contains` array/params argument in a convention predicate |
| [SVCFY012](#svcfy012) | Error | Unsupported predicate expression shape in a `ByNamespace`/`ByNamespaceOf`/`ByTypeName` predicate |
| [SVCFY013](#svcfy013) | Error | Decorator does not implement the decorated service interface |
| [SVCFY014](#svcfy014) | Error | `[Decorator]` applied to a class that does not implement exactly one interface |
| [SVCFY015](#svcfy015) | Warning | Decorator declared both via `[DecoratorFor<T>]`/`[Decorator]` and `.Decorate<,>()` for the same service — duplicate dropped |
| [SVCFY016](#svcfy016) | Error | `ByBaseType(Type, ...)` called with a non-generic `typeof(IFoo)` — the overload requires a generic type |

> `SVCFY004` ("ambiguous service type") no longer exists as a separate code — classes implementing
> multiple interfaces are registered against **all** of them instead.

## SVCFY001

`[Add]`, `[AddSelf]` (and the generic forms) require an explicit [`Lifetime`](byconfiguration/lifetime.md)
argument.

```csharp
[Add] // SVCFY001 — missing Lifetime
public class UserService : IUserService { }

[Add(Lifetime.Scoped)] // OK
public class UserService : IUserService { }
```

## SVCFY002

An attribute with an explicit service type (`Type` argument or `<TService>` generic parameter) requires
the decorated class to implement that type.

```csharp
[AddScoped(typeof(IOrderService))] // SVCFY002 — UserService doesn't implement IOrderService
public class UserService : IUserService { }
```

## SVCFY003

A no-explicit-type attribute (`[AddScoped]`, `[AddKeyedScoped("key")]`, …) requires the decorated class to
implement at least one interface — otherwise there is nothing to register it as.

```csharp
[AddScoped] // SVCFY003 — UserService implements no interfaces
public class UserService { }
```

> A class implementing two or more interfaces registers against **all** of them (one registration per
> interface, same lifetime/key) — this is not an error.

## SVCFY005

[`[Configure("Section", Lifetime.Singleton)]`](byconfiguration/configure.md) requires both a non-empty
section name and a lifetime.

```csharp
[Configure] // SVCFY005 — missing section name and lifetime
public class MyOptions { }
```

## SVCFY006

[`[Configure]`](byconfiguration/configure.md) requires the decorated class to have a parameterless
constructor, since `IOptions<T>` binding relies on it.

```csharp
[Configure("MySection", Lifetime.Singleton)]
public class MyOptions
{
    public MyOptions(string required) { } // SVCFY006 — no parameterless constructor
}
```

## SVCFY007

A [`[DecoratorFor<TService>]`](byconfiguration/decorator-for.md) / `.Decorate<,>()` decorator must have
a public constructor with a parameter of the decorated interface type — that's how the generator wires
the "inner" instance into the decorator.

```csharp
public interface IUserService { }

// SVCFY007 — LoggingDecorator has no constructor accepting IUserService
[DecoratorFor<IUserService>]
public class LoggingDecorator : IUserService
{
    public LoggingDecorator(ILogger logger) { }
}
```

## SVCFY008

A class used as a [`[DecoratorFor<TService>]`](byconfiguration/decorator-for.md) / `.Decorate<,>()`
decorator must not be registered with a **non-keyed** `[Add*]` attribute — decorators are wired into the
keyed chain (`AddKeyedXxx("__BASE__")` / `"<TypeName>"`) by the generator. Use `[AddKeyed*]`, or no
registration attribute at all, on the decorator class.

```csharp
public interface IUserService { }

[DecoratorFor<IUserService>]
[AddScoped] // SVCFY008 — LoggingDecorator is a decorator, must not use a non-keyed [Add*]
public class LoggingDecorator : IUserService { }
```

## SVCFY009

[`[AddSelfScoped]`](byconfiguration/add-self-scoped.md),
[`[AddSelfTransient]`](byconfiguration/add-self-transient.md),
[`[AddSelfSingleton]`](byconfiguration/add-self-singleton.md) and
[`[AddSelf(Lifetime)]`](byconfiguration/add-self.md) require a concrete, instantiable class.

```csharp
[AddSelfScoped] // SVCFY009 — abstract classes cannot be registered with themselves as the service type
public abstract class JobBase { }
```

## SVCFY010

[`ByBaseType<TBase>(lifetime, ServiceTypeSelector.Self)`](conventions/by-base-type.md) registers each
matched type with **itself** as the service type and ignores `TBase` entirely — so `TBase` must be a
**class**. An interface `TBase` carries no usable "self" type for this selector.

```csharp
// SVCFY010 — IJob is an interface and cannot be used with ServiceTypeSelector.Self
services.AddServicefyConventions()
    .ByBaseType<IJob>(Lifetime.Singleton, ServiceTypeSelector.Self);
```

Use `ServiceTypeSelector.BaseType`, `ImplementedInterfaces`, or `SelfWithInterfaces` instead, or change
`TBase` to a class.

## SVCFY011

In a [`ByNamespace`](conventions/by-namespace.md#supported-predicates),
[`ByNamespaceOf`](conventions/by-namespace-of.md) or [`ByTypeName`](conventions/by-type-name.md#supported-predicates)
predicate, the array/params argument to `StartsWith`, `EndsWith` or `Contains` must contain only string
literals so Servicefy can evaluate it at generation time. The call site is ignored.

```csharp
// SVCFY011 — "Repository" + suffix is not a string literal
services.AddServicefyConventions()
    .ByNamespace(ns => ns.EndsWith(["Repository", "Repository" + suffix]), Lifetime.Scoped);
```

## SVCFY012

In a [`ByNamespace`](conventions/by-namespace.md#supported-predicates),
[`ByNamespaceOf`](conventions/by-namespace-of.md) or [`ByTypeName`](conventions/by-type-name.md#supported-predicates)
predicate, the lambda body has a shape Servicefy cannot evaluate at generation time. The call site is
ignored.

```csharp
// SVCFY012 — method call on the predicate parameter is not a supported string method
services.AddServicefyConventions()
    .ByNamespace(ns => ns.ToUpperInvariant().StartsWith("MYAPP"), Lifetime.Scoped);
```

## SVCFY013

A [`[DecoratorFor<TService>]`](byconfiguration/decorator-for.md) / `.Decorate<,>()` decorator must
implement `TService` — the generator wires it into the keyed chain as an `TService` instance, so it
must be assignable to that interface.

```csharp
public interface IService001 { }
public interface IService002 { }

// SVCFY013 — Service002Decorator implements IService002, not IService001
[DecoratorFor<IService001>]
public class Service002Decorator : IService002
{
    public Service002Decorator(IService001 inner) { }
}
```

## SVCFY014

[`[Decorator]`](byconfiguration/decorator.md) infers the decorated service type (`TService`) from the
**single** interface implemented by the decorator class. If the class implements zero or two-or-more
interfaces, there is nothing — or too much — to infer from, so the call site is reported as an error.

```csharp
public interface IService001 { }
public interface IService002 { }

// SVCFY014 — LoggingDecorator implements 2 interfaces, [Decorator] cannot infer TService
[Decorator]
public class LoggingDecorator : IService001, IService002
{
    public LoggingDecorator(IService001 inner) { }
}
```

Use [`[DecoratorFor<TService>]`](byconfiguration/decorator-for.md) instead to specify the target
interface explicitly.

## SVCFY015

The same decorator class is declared **twice** for the same service interface — once via
[`[DecoratorFor<TService>]`](byconfiguration/decorator-for.md) / [`[Decorator]`](byconfiguration/decorator.md),
and once via `.Decorate<TService, TDecorator>()`. The duplicate declaration is dropped from the
generated chain and the decorator is applied only once.

```csharp
public interface IReader { void Read(); }

// LoggingDecorator is declared as an inner/base layer here...
[Decorator]
public class LoggingDecorator(IReader inner) : IReader
{
    public void Read() => inner.Read();
}

// ...AND added again as an outer layer here — SVCFY015, the second declaration is ignored.
services.AddServicefyConventions()
    .ByBaseType<IReader>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)
    .Decorate<IReader, LoggingDecorator>();
```

Without the dedup, `LoggingDecorator` would be emitted **twice** under the same keyed registration
(`AddKeyedScoped<IReader>("...LoggingDecorator", ...)`), and the second factory would resolve
`IReader` using that same key — i.e. it would resolve **itself**, causing infinite recursion (stack
overflow) the first time `IReader` is requested.

Declare the decorator only one way: either with `[DecoratorFor<TService>]` / `[Decorator]`, or with
`.Decorate<TService, TDecorator>()` — not both.

## SVCFY016

The [`ByBaseType(Type, ...)` overload](conventions/by-base-type.md#open-generics) was called with a
**non-generic** `typeof(...)`. That overload exists to accept generic types — open
(`typeof(IRepository<>)`) or closed (`typeof(IRepository<Order>)`); a non-generic type has nothing to
scan generically. The call site is ignored.

```csharp
public interface IFoo { }

// SVCFY016 — IFoo is not generic; use the generic overload instead.
services.AddServicefyConventions()
    .ByBaseType(typeof(IFoo), Lifetime.Scoped);
```

Use the generic form for non-generic types:

```csharp
.ByBaseType<IFoo>(Lifetime.Scoped);
```
