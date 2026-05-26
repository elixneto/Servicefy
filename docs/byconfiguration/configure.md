---
title: Configure
---

# Configure

[← Back to ByConfiguration](index.md)

Binds a configuration section to the decorated class and registers it directly as `T` (not
`IOptions<T>`), with the given [`Lifetime`](lifetime.md). The generated code differs by lifetime so
that **Scoped** and **Transient** registrations pick up `appsettings.json` reloads at runtime, while
**Singleton** stays a simple one-time bind.

## Signature

```csharp
[Configure("Smtp", Lifetime.Singleton)]
public class SmtpOptions
{
    public string Host { get; set; }
    public int Port { get; set; }
}
```

| Parameter | Description |
|-----------|-------------|
| `sectionName` | The name of the configuration section to bind, e.g. `"Smtp"`. |
| `lifetime` | The desired registration [`Lifetime`](lifetime.md) for the bound type. |

## Generated code

### Scoped / Transient — reload-aware via `IOptionsMonitor<T>`

```csharp
[Configure("TenantSettings:Database", Lifetime.Transient)]
public class TenantDatabaseConfig
{
    public string ConnectionString { get; set; }
}
```
```csharp
// generated
services.Configure<TenantDatabaseConfig>(configuration.GetSection("TenantSettings:Database"));
services.AddTransient(sp => sp.GetRequiredService<IOptionsMonitor<TenantDatabaseConfig>>().Value);
```

Each time a `TenantDatabaseConfig` is resolved, it comes from `IOptionsMonitor<T>.Value`, which
reflects the **current** state of `"TenantSettings:Database"` — including changes made to
`appsettings.json` at runtime (when the configuration provider has `reloadOnChange: true`). For
`Lifetime.Scoped`, the only difference is `services.AddScoped(...)` instead of `AddTransient(...)`.

### Singleton — bound once

```csharp
[Configure("Smtp", Lifetime.Singleton)]
public class SmtpOptions
{
    public string Host { get; set; }
    public int Port { get; set; }
}
```
```csharp
// generated
services.AddSingleton(_ => configuration.GetSection("Smtp").Get<SmtpOptions>()
    ?? throw new InvalidOperationException("Unable to bind section 'Smtp' to type SmtpOptions."));
```

A singleton is constructed once and held for the lifetime of the application — wiring it through
`IOptionsMonitor<T>` would add overhead without giving any benefit, since the singleton instance
itself would never reflect a later config reload anyway. So singletons bind the section once, at
registration time, and throw immediately if the section can't be bound.

## Dependencies

`services.Configure<T>(IConfiguration)` and `IOptionsMonitor<T>` come from
[`Microsoft.Extensions.Options.ConfigurationExtensions`](https://www.nuget.org/packages/Microsoft.Extensions.Options.ConfigurationExtensions)
— add this package to any project using `[Configure(..., Lifetime.Scoped)]` or
`[Configure(..., Lifetime.Transient)]`. It's commonly already present transitively in ASP.NET Core /
generic-host applications.

## Diagnostics

- [`SVCFY005`](../diagnostics.md#svcfy005) — `sectionName` or `lifetime` is omitted.
- [`SVCFY006`](../diagnostics.md#svcfy006) — the class has no parameterless constructor (required for
  options binding).

## See also

- [Lifetime](lifetime.md)
- [Diagnostics](../diagnostics.md)
