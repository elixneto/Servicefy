---
title: AddSelfTransient
---

# AddSelfTransient

[← Back to ByConfiguration](index.md)

Registers the decorated class **with itself as the service type** and a **Transient** lifetime
(`services.AddTransient<TImpl>()`). Implemented interfaces, if any, are ignored.

## Signature

```csharp
[AddSelfTransient]
public class ReportBuilder { }
```
```csharp
// generated
services.AddTransient<ReportBuilder>();
```

**Diagnostics:** [`SVCFY009`](../diagnostics.md#svcfy009) if applied to an abstract or static class.

## See also

- [AddSelfScoped](add-self-scoped.md), [AddSelfSingleton](add-self-singleton.md) — other fixed lifetimes
- [AddSelf](add-self.md) — generic form with an explicit [`Lifetime`](lifetime.md)
- [AddTransient](add-transient.md) — interface-based equivalent
