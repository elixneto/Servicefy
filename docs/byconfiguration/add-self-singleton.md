---
title: AddSelfSingleton
---

# AddSelfSingleton

[← Back to ByConfiguration](index.md)

Registers the decorated class **with itself as the service type** and a **Singleton** lifetime
(`services.AddSingleton<TImpl>()`). Implemented interfaces, if any, are ignored.

## Signature

```csharp
[AddSelfSingleton]
public class MetricsCollector { }
```
```csharp
// generated
services.AddSingleton<MetricsCollector>();
```

**Diagnostics:** [`SVCFY009`](../diagnostics.md#svcfy009) if applied to an abstract or static class.

## See also

- [AddSelfScoped](add-self-scoped.md), [AddSelfTransient](add-self-transient.md) — other fixed lifetimes
- [AddSelf](add-self.md) — generic form with an explicit [`Lifetime`](lifetime.md)
- [AddSingleton](add-singleton.md) — interface-based equivalent
