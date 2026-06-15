# Contributing to Servicefy

Thanks for considering a contribution. Servicefy is a Roslyn source generator, so most changes
live in `src/Servicefy.Package` and are verified by running the generator against in-memory
source code in `tests/Servicefy.Tests`.

## Requirements

- .NET SDK 8.0 or later (the package multi-targets `net8.0` and `net10.0`; `net8.0` is enough
  for local development)
- C# 12+

## Project layout

- `src/Servicefy.Package` — the generator itself:
  - `ByConfiguration/` — attribute-driven registration (`[AddScoped]`, `[Configure]`, etc.)
  - `Conventions/` — `ByNamespace`, `ByBaseType`, `ByNamespaceOf`, `ByTypeName`
  - `Decorators/` — `[DecoratorFor<T>]` / `.Decorate<,>()` chain generation
  - `Diagnostics/` — `SVCFYxxx` diagnostic definitions
- `tests/Servicefy.Tests` — xUnit (v3) tests. Most tests compile a small source snippet via
  `Helpers/CompilationHelper.RunGenerator(source)` and assert on the generated syntax trees
  (`*.g.cs`) and/or reported diagnostics.
- `docs/` — the GitHub Pages documentation. This is the canonical reference for behavior, so it
  should stay in sync with any user-facing change.

## Building and testing

From the repo root:

```bash
dotnet build
dotnet test
```

Both commands run against `Servicefy.sln`.

## Making a change

1. Fork the repo and create a branch off `main`.
2. Implement the change in `src/Servicefy.Package`.
3. Add or update tests in `tests/Servicefy.Tests`.
4. If the change affects public API, attributes, conventions, or diagnostics, update the
   matching page(s) under `docs/`.
5. Run `dotnet test` and make sure everything passes.
6. Open a pull request against `main`, describing what changed and why.

## Adding a new diagnostic

New diagnostics get the next free `SVCFYxxx` ID and must be documented in
[`docs/diagnostics.md`](docs/diagnostics.md), including the condition that triggers it and an
example.

## Reporting bugs / requesting features

Please open an issue with a minimal repro — a few classes plus the attributes or conventions
involved — since most bugs come down to what code gets generated for a given input.
