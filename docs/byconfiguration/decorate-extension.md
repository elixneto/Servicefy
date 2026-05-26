---
title: .Decorate<TService, TDecorator>()
---

# .Decorate&lt;TService, TDecorator&gt;()

[← Back to ByConfiguration](index.md)

Extension that adds `TDecorator` as an additional **outer** decorator layer for `TService`.
Available on `IServiceCollection` (ByConfiguration) and on `IServicefyConventionsBuilder`
(see [Conventions](../conventions/index.md)).

```csharp
services.AddServicefy(config)
    .Decorate<IUserService, LoggingDecorator>()
    .Decorate<IUserService, CacheDecorator>();
```

Calls are merged with any [`[DecoratorFor<TService>]`](decorator-for.md) classes for the same
`TService`: `.Decorate<,>()` calls form the **outer** layers in **declaration order** (the first call
declared is the outermost decorator), and `[DecoratorFor<T>]` classes form the inner/base layers,
sorted by fully-qualified type name. See [DecoratorFor&lt;T&gt;](decorator-for.md) for the full merge
rule, generated chain, and diagnostics (`SVCFY007`/`SVCFY008`/`SVCFY013`).

## See also

- [DecoratorFor<T>](decorator-for.md)
- [Decorator](decorator.md)
- [Conventions](../conventions/index.md)
