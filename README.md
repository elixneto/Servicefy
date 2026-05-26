# Servicefy

**Zero-reflection dependency injection registration via compile-time source generation.**

Servicefy is a [Roslyn Source Generator](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) that replaces manual `services.Add*<TService, TImpl>()` calls with simple attributes on your classes. All code is generated at compile time — no runtime reflection, no performance overhead, no magic.

[![NuGet](https://img.shields.io/nuget/v/Servicefy.svg)](https://www.nuget.org/packages/Servicefy)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## Installation

```bash
dotnet add package Servicefy
```

> Compatible with any SDK-style project targeting `netstandard2.0` or later (.NET Framework 4.6.1+, .NET Core 2.0+, .NET 5+). All attributes work from **C# 8** onward. The short generic syntax `[AddScoped<IFoo>]` requires **C# 11+** and is automatically omitted by the generator on older versions — no configuration needed; use `[AddScoped(typeof(IFoo))]` as a drop-in alternative.

---

## Quick start

Annotate your service implementations:

```csharp
[AddScoped]
public sealed class UserService : IUserService { }

[AddTransient]
public sealed class EmailNotificationHandler : INotificationHandler { }

[Configure("App:Database", Lifetime.Singleton)]
public sealed class DatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public int MaxPoolSize { get; set; }
}
```

Then register everything with a single call:

```csharp
// Program.cs
builder.Services.AddServicefy(builder.Configuration);
```

Servicefy generates the `AddServicefy` extension method for you at build time, scoped to your assembly's namespace — no manual wiring required.

---

## Attributes

| Attribute | Equivalent |
|---|---|
| `[AddScoped]` | `services.AddScoped<IFoo, Foo>()` |
| `[AddSingleton]` | `services.AddSingleton<IFoo, Foo>()` |
| `[AddTransient]` | `services.AddTransient<IFoo, Foo>()` |
| `[AddScoped(typeof(IFoo))]` | `services.AddScoped<IFoo, Foo>()` (explicit contract, C# 7+) |
| `[AddScoped<IFoo>]` | `services.AddScoped<IFoo, Foo>()` (explicit contract, C# 11+) |
| `[Add(Lifetime.Scoped)]` | `services.AddScoped<IFoo, Foo>()` |
| `[Add(Lifetime.Scoped, typeof(IFoo))]` | `services.AddScoped<IFoo, Foo>()` (explicit contract, C# 7+) |
| `[Configure("Section", Lifetime.Singleton)]` | `services.AddSingleton(_ => config.GetSection(...).Get<T>())` |

### Service type inference

- **Inferred** (`[AddScoped]`): Servicefy infers the service type from the single interface the class implements. If the class implements zero or more than one interface, a compile-time error is raised.
- **Explicit via `typeof`** (`[AddScoped(typeof(IFoo))]`): Registers against the specified contract. Works on **C# 7+**. Ideal for projects not yet on C# 11, or when targeting multiple contracts on the same class.
- **Explicit via generic** (`[AddScoped<IFoo>]`): Same behavior, shorter syntax. Requires **C# 11+**. The generator detects the language version automatically and omits the generic attribute variants on C# 8–10, so the package compiles cleanly regardless of target version.
- **Multiple registrations**: Stack multiple attributes on the same class to register it under several contracts.

```csharp
// C# 7+
[AddScoped(typeof(IUserReader))]
[AddScoped(typeof(IUserWriter))]
public class UserRepository : IUserReader, IUserWriter { }

// C# 11+
[AddScoped<IUserReader>]
[AddScoped<IUserWriter>]
public class UserRepository : IUserReader, IUserWriter { }
```

---

## Multi-assembly aggregation

In layered or modular architectures, each assembly that uses Servicefy attributes gets its own generated `AddServicefy()`. Beyond that, Servicefy **automatically detects** when a referenced assembly already has a generated `AddServicefy()` and wires the aggregation chain for you — no configuration needed.

### How it works

```
MyApp.Feature1          →  generates  MyApp.Feature1.ServicefyExtensions.AddServicefy()
MyApp.Feature2          →  generates  MyApp.Feature2.ServicefyExtensions.AddServicefy()

MyApp.Infrastructure    →  references Feature1.csproj + Feature2.csproj
                        →  generates  MyApp.Infrastructure.ServicefyExtensions.AddServicefy()
                               └─ calls Feature1.ServicefyExtensions.AddServicefy()
                               └─ calls Feature2.ServicefyExtensions.AddServicefy()

MyApp.API               →  references Infrastructure only
                        →  generates  MyApp.API.ServicefyExtensions.AddServicefy()
                               └─ calls Infrastructure.ServicefyExtensions.AddServicefy()
```

A single call in `Program.cs` propagates registrations through the entire dependency chain:

```csharp
builder.Services.AddServicefy(builder.Configuration); // registers Feature1 + Feature2 transitively
```

### Double-registration protection

Servicefy decorates each generated `ServicefyExtensions` with a `[ServicefyAggregates(...)]` marker listing the namespaces it already covers. When building an aggregator, Servicefy reads these markers from all direct references and automatically excludes any namespace already covered further down the chain.

This means that even if a project directly references both an aggregator assembly and one of the assemblies it aggregates, Servicefy will emit only the top-level call — eliminating the risk of double-registration regardless of project structure.

---

## Diagnostics

Servicefy reports the following compile-time errors:

| Code       | Description |
|------------|---|
| `SVCFY001` | `[Add]` attribute used without specifying a `Lifetime`. |
| `SVCFY002` | Generic `[AddScoped<T>]` used but the class does not implement `T`. |
| `SVCFY003` | Non-generic `[AddScoped]` used on a class that implements no interfaces. |
| `SVCFY004` | Non-generic `[AddScoped]` used on a class that implements multiple interfaces — ambiguous. |
| `SVCFY005` | `[Configure]` attribute is missing `sectionName` or `Lifetime`. |
| `SVCFY006` | `[Configure]` used on a class without a parameterless constructor. |

---

## License

MIT — see [LICENSE](LICENSE).
