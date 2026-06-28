# Servicefy

**Zero-reflection DI registration via compile-time source generation.**

[![NuGet](https://img.shields.io/nuget/v/Servicefy.svg)](https://www.nuget.org/packages/Servicefy)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

📖 **[Full documentation](https://elixneto.github.io/Servicefy/)**

Servicefy replaces manual `services.Add*<TService, TImpl>()` calls with attributes or namespace conventions. Everything is generated at build time — no runtime reflection, no performance overhead, AOT-safe.

**Requirements:** .NET 8+ · C# 12+

---

## Installation

```bash
dotnet add package Servicefy
```

---

## By configuration (attributes)

Annotate your classes and call `AddServicefy` once:

```csharp
[AddScoped]
public class UserService : IUserService { }

[AddTransient]
public class EmailSender : IEmailSender { }

[AddKeyedScoped("primary")]
public class PrimaryRepository : IRepository { }

[Configure("App:Database", Lifetime.Singleton)]
public class DatabaseSettings { public string ConnectionString { get; set; } = ""; }
```

```csharp
// Program.cs
builder.Services.AddServicefy(builder.Configuration);
```

Multiple attributes on the same class register it under multiple contracts:

```csharp
[AddScoped<IUserReader>]
[AddScoped<IUserWriter>]
public class UserRepository : IUserReader, INotImplemented, IUserWriter { }
```

---

## By conventions

Register every class in matching namespaces against its directly implemented interfaces — no attributes needed:

```csharp
// Program.cs
builder.Services.AddServicefyConventions()
    .ByNamespace(ns => ns.StartsWith("MyApp.Features"), Lifetime.Scoped)
    .ByNamespaceOf<SomeTypeInMyApp.Data.Marker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped)
    .ByTypeName((ns, name) => name.EndsWith("Repository") && ns.Equals("MyApp.Implementations"), Lifetime.Scoped);
```

Classes with no interfaces, abstract/static classes and open generics are skipped. A class already registered via attributes (above) is never registered again here.

`ByNamespaceOf<TMarker>` behaves like `ByNamespace`, but only considers types in `TMarker`'s namespace (or a sub-namespace of it) before applying the predicate — handy for scoping a convention to one module without hardcoding its namespace as a string. `ByTypeName` matches on both namespace and type name.

`ByBaseType` registers every class assignable to a base type or interface, with a `ServiceTypeSelector` controlling what each match is registered as. It also has a `typeof(IFoo<>)` overload for **open generics** — an open implementation like `Repository<T> : IRepository<T>` is registered as `Add(typeof(IRepository<>), typeof(Repository<>))`, so any `IRepository<Order>` resolves to `Repository<Order>`:

```csharp
builder.Services.AddServicefyConventions()
    .ByBaseType<IService>(Lifetime.Scoped)                              // closed/non-generic
    .ByBaseType(typeof(IRepository<>), Lifetime.Scoped);               // open generic
```

See [ByBaseType › Open generics](docs/conventions/by-base-type.md#open-generics) for matching rules, selectors and AOT-safe open-generic decorators.

---

## Decorator pattern

Mark a decorator class with `[DecoratorFor<TService>]` (or `[Decorator]` to infer `TService` from the
single interface it implements), and/or add outer layers fluently with
`.Decorate<TService, TDecorator>()`. Works whether the underlying service is registered by attribute
or by convention.

```csharp
public interface IUserService { }

[AddScoped]
public class UserService : IUserService { }

public class CacheDecorator : IUserService
{
    public CacheDecorator(IUserService service) { }
}

public class LoggingDecorator : IUserService
{
    public LoggingDecorator(IUserService service, ILogger logger) { }
}
```

```csharp
// Program.cs
builder.Services.AddServicefy()
    .Decorate<IUserService, CacheDecorator>()     // outermost (first to be executed)
    .Decorate<IUserService, LoggingDecorator>();
```

---

## License

MIT — see [LICENSE](LICENSE).
