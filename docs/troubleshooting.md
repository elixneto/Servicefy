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
