# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v1.2.0.html).

## [1.2.0] - 2026-11-14

### Added

- **Conventions can register types from NuGet packages** (issue #2: #7, #8, #9).
  - `ByBaseType<TBase>()`: when `TBase` is declared in a NuGet package, that package (not its
    dependencies) is also scanned. Its public concrete classes are candidates, and its interfaces are
    registrable services, so `class OrderRepository : MyCompany.Contracts.IRepository<Order>` is now
    registered against the package interface.
  - `ByNamespaceOf<TMarker>()`: when `TMarker` is declared in a NuGet package, `TMarker`'s root
    namespace subtree of that package is also scanned, and its interfaces are registrable services.
  - **`ConventionScope`** enum (`Compilation`, the default, and `AllReferences`) with a new optional
    `scope` parameter on `ByNamespace` and `ByTypeName`. `ConventionScope.AllReferences` also scans every
    referenced NuGet package, transitive dependencies included. A `StartsWith`-bounded `ByNamespace`
    predicate prunes the walk to the matching namespaces of each package.
- **Diagnostic `SVCFY017`** (warning): `ConventionScope.AllReferences` combined with a predicate that
  matches (almost) everything (`_ => true`, `(ns, name) => true`, or a `StartsWith` bound shorter than
  4 characters).
- **`StartsWith` / `EndsWith` / `Contains(string[], StringComparison)` overloads** in
  `ServicefyStringExtensions` (issue #17), so the documented
  `ns.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase)` form compiles.
  `Method(StringComparison.X, [...])` is also recognized by the predicate parsers.

### Changed

- **Registrations can change for existing code.** `ByBaseType<TBase>()` with a NuGet-declared `TBase`
  used to register nothing for interfaces from that package; it now registers them, together with the
  package's own public implementations. The same applies to `ByNamespaceOf<TMarker>()` with a
  NuGet-declared marker. Use `matchAttribute`, a more specific `TBase` or a narrower predicate if that
  is not what you want.
- A `StartsWith` / `EndsWith` / `Contains` array argument followed by anything other than a
  `StringComparison.X` literal now fails to parse (`SVCFY012`) instead of matching with `Ordinal`.
- Faster incremental generation (issues #4 to #6): convention rules are value-equatable pipeline
  models, and conventions and decorators share one cached type pool, so referenced assemblies are walked
  once per reference set instead of on every edit.

### Fixed

- Docs and XML docs now describe the supported `StringComparison` forms and say that unsupported
  predicate shapes report `SVCFY012`.

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

[1.2.0]: https://github.com/elixneto/Servicefy/releases/tag/v1.2.0
[1.1.0]: https://github.com/elixneto/Servicefy/releases/tag/v1.1.0
[1.0.0]: https://github.com/elixneto/Servicefy/releases/tag/v1.0.0
