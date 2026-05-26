---
title: AddSelfScoped
---

# AddSelfScoped

[← Back to ByConfiguration](index.md)

Registers the decorated class **with itself as the service type** and a **Scoped** lifetime
(`services.AddScoped<TImpl>()`). Implemented interfaces, if any, are ignored.

## Signature

```csharp
[AddSelfScoped]
public class RequestContext { }
```
```csharp
// generated
services.AddScoped<RequestContext>();
```

**Diagnostics:** [`SVCFY009`](../diagnostics.md#svcfy009) if applied to an abstract or static class.

## See also

- [AddSelfSingleton](add-self-singleton.md), [AddSelfTransient](add-self-transient.md) — other fixed lifetimes
- [AddSelf](add-self.md) — generic form with an explicit [`Lifetime`](lifetime.md)
- [AddScoped](add-scoped.md) — interface-based equivalent
