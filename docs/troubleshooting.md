---
title: Troubleshooting
---

# Troubleshooting
[← Back to Servicefy](./index.md)

## IDE (Rider / Visual Studio) issues with generated code

Rider and Visual Studio don't always resolve Roslyn source-generator output reliably — symptoms
include "Go to Definition" not working on generated members (e.g. `ServicefyConventionsBuilder`,
`AddServicefy`, `AddServicefyConventions`), red squiggles on types that actually compile fine, or
stale generated code after editing a `[Add*]` attribute or a `.ByNamespace(...)` / `.ByBaseType<T>(...)`
call.

When that happens, it's faster to dump the generator's output to disk and read it directly than to
fight the IDE's generator cache.

### Check cached files
**Rider** — Dependencies > .NET X.Y > Source Generators > Servicefy.Package.*
\
**VS** — Dependencies > Analyzers >  Servicefy.Package > Servicefy.Package.*

### Dump generated files to a folder

Add to the project's `.csproj`:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>

<ItemGroup>
    <!-- Exclude the dumped .g.cs files from compilation, otherwise they're recompiled as regular
         sources and duplicate the types the generator itself emits. -->
    <Compile Remove="Generated/**/*.cs" />
</ItemGroup>
```

After a build, every generator's output lands under:

```
Generated/Servicefy.Package/<GeneratorName>/*.g.cs
```

Add `Generated/` to `.gitignore` — it's build output, regenerated on every build.

### If the IDE still shows stale results
— the IDE caches the previous generator output independently of `obj/`.

```
dotnet clean
rm -rf bin obj Generated   # or Remove-Item -Recurse -Force bin,obj,Generated
dotnet build
```

**Rider** — File > Invalidate Caches > Restart
\
**VS** — Restarting the VS analyzer host (or the IDE itself) 

## BenchmarkDotNet — "duplicate class" errors

By default, [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) builds each benchmark into a **separate generated project** and compiles
it from scratch. Since Servicefy is a Roslyn source generator, it runs again during that build — and
ends up emitting the same generated types (e.g. `AddScopedAttribute`, `Lifetime`,
`ServicefyExtensions`) that already exist in the assembly being benchmarked, which the new project also
references. The result is a compile error about duplicate/ambiguous types.

To avoid this, run the benchmark **in-process** instead of building an isolated project:

```csharp
var config = ManualConfig
    .Create(DefaultConfig.Instance)
    .AddJob(
      Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance)
    );

BenchmarkRunner.Run<MyBenchmarkClass>(config);
```

- **`InProcessNoEmitToolchain.Instance`** — runs the benchmark in the current process instead of
  generating and compiling a separate project. Since no new compilation happens, the source generator
  doesn't run a second time and there's no duplicate-type conflict.

### Caveats of running in-process

- **No process isolation.** The default toolchain runs each benchmark in a fresh, clean process.
  In-process, everything runs inside the runner's own process, so GC activity and any `static` state
  from one benchmark can bleed into another. Watch out for benchmarks that cache singletons or other state in `static`
  fields.
- **No multi-runtime comparisons.** You can't compare `net8.0` vs `net9.0`, etc. in the same run —
  everything executes on the runtime of the host process.

These caveats mostly affect *absolute* numbers and edge cases. The impact is minimal as long as you run from a
Release build.
