---
title: AddSelf
---

# AddSelf

[← Back to ByConfiguration](index.md)

Registers the decorated class **with itself as the service type**
(`services.Add{Lifetime}<TImpl>()`), with an explicit [`Lifetime`](lifetime.md). Implemented
interfaces, if any, are ignored.

## Signature

```csharp
[AddSelf(Lifetime.Singleton)]
public class MetricsCollector { }
```
```csharp
// generated
services.AddSingleton<MetricsCollector>();
```

| Parameter | Description |
|-----------|-------------|
| `lifetime` | The desired registration [`Lifetime`](lifetime.md). |

**Diagnostics:**
[`SVCFY001`](../diagnostics.md#svcfy001) if `lifetime` is omitted ·
[`SVCFY009`](../diagnostics.md#svcfy009) if applied to an abstract or static class.

## See also

- [AddSelfScoped](add-self-scoped.md), [AddSelfSingleton](add-self-singleton.md), [AddSelfTransient](add-self-transient.md) — fixed-lifetime shorthands
- [Add](add.md) — equivalent for interface-based registration
- [Lifetime](lifetime.md)
- [Conventions → ByBaseType](../conventions/by-base-type.md) with `ServiceTypeSelector.Self` — convention-based equivalent
