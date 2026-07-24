# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-07-24

### Added

- **Open-generic support in `ByBaseType` conventions** (issue #1). Common patterns such as
  `Repository<T> : IRepository<T>`, `IValidator<T>` and generic handlers can now be registered
  by convention.
- **New `typeof()`-based overload**
  `ByBaseType(Type openGenericBaseType, Lifetime lifetime, ServiceTypeSelector selector = BaseType, Type matchAttribute = null)`,
  called as `.ByBaseType(typeof(IRepository<>), Lifetime.Scoped)` — an unbound generic is not a valid
  type argument for the generic `ByBaseType<TBase>()` form.
- **Open-generic decorators** via `.Decorate(typeof(IRepository<>), typeof(LoggingRepository<>))`,
  applied to the closed forms known at compile time (runtime-only closed forms are not decorated — AOT limitation).
- **Diagnostic `SVCFY016`** — a non-generic `typeof(IFoo)` passed to the `Type` overload is reported and
  ignored; use the generic `ByBaseType<IFoo>()` form instead.

### Changed

- Convention matching compares open generics via `OriginalDefinition`, so open-generic implementations
  are emitted through the non-generic `IServiceCollection` overloads
  (`services.AddScoped(typeof(IRepository<>), typeof(Repository<>))`), while concrete closed
  implementations are registered against their constructed interface.


## [1.0.0] - Initial release

[1.1.0]: https://github.com/elixneto/Servicefy/releases/tag/v1.1.0
[1.0.0]: https://github.com/elixneto/Servicefy/releases/tag/v1.0.0
