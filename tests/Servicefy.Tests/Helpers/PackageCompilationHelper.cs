using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;

namespace Servicefy.Tests.Helpers;

internal enum ReferenceKind
{
    Package,

    Project,
}

internal sealed record ReferencedAssembly(
    string AssemblyName,
    string Source,
    ReferenceKind Kind = ReferenceKind.Package,
    string? FilePath = null)
{
    public static ReferencedAssembly Package(string assemblyName, string source, string? filePath = null) =>
        new(assemblyName, source, ReferenceKind.Package, filePath);

    public static ReferencedAssembly Project(string assemblyName, string source) =>
        new(assemblyName, source, ReferenceKind.Project);

    public string ResolvedFilePath => FilePath ?? Kind switch
    {
        ReferenceKind.Package =>
            $"/home/test/.nuget/packages/{AssemblyName.ToLowerInvariant()}/1.0.0/lib/net8.0/{AssemblyName}.dll",
        _ => $"/src/{AssemblyName}/bin/Debug/net8.0/{AssemblyName}.dll",
    };
}

internal sealed record PackageRunResult(
    Compilation Output,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    string? ByBaseTypeSource)
{
    public Compilation? Input { get; init; }

    public IReadOnlyList<Diagnostic> CompilationErrors =>
        Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    public string? GeneratedSource(string fileName) =>
        Output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith(fileName, StringComparison.Ordinal))
            ?.ToString();

    public string? ByNamespaceOfSource => GeneratedSource("ServicefyConventionsBuilder.ByNamespaceOf.g.cs");

    public string? NamespaceOfBranch(string markerTypeFqn, string? lifetime = null)
    {
        var source = ByNamespaceOfSource;
        if (source is null) return null;

        var header = lifetime is null
            ? $"if (markerType == typeof({markerTypeFqn}) &&"
            : $"if (markerType == typeof({markerTypeFqn}) && lifetime == Lifetime.{lifetime} &&";

        var start = source.IndexOf(header, StringComparison.Ordinal);
        if (start < 0) return null;

        var end = source.IndexOf("return;", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    public string? Branch(string baseTypeFqn, string? selector = null)
    {
        if (ByBaseTypeSource is null) return null;

        var header = $"if (baseType == typeof({baseTypeFqn}) &&";
        var searchFrom = 0;
        while (true)
        {
            var start = ByBaseTypeSource.IndexOf(header, searchFrom, StringComparison.Ordinal);
            if (start < 0) return null;

            var end = ByBaseTypeSource.IndexOf("return;", start, StringComparison.Ordinal);
            var branch = end < 0 ? ByBaseTypeSource[start..] : ByBaseTypeSource[start..end];
            var headerLineEnd = branch.IndexOf('\n');
            var headerLine = headerLineEnd < 0 ? branch : branch[..headerLineEnd];

            if (selector is null || headerLine.Contains($"selector == ServiceTypeSelector.{selector} ", StringComparison.Ordinal))
                return branch;

            searchFrom = start + header.Length;
        }
    }
}

internal static class PackageCompilationHelper
{
    public const string AssemblyName = "TestAssembly";

    public const string SharedFrameworkFolder = "/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.0/";

    private static readonly Lazy<ImmutableArray<MetadataReference>> FrameworkReferences = new(() =>
    [
        .. new[]
            {
                typeof(object).Assembly.Location,
                Assembly.Load("System.Runtime").Location,
                Assembly.Load("netstandard").Location,
                Assembly.Load("System.ComponentModel").Location,
            }
            .Distinct(StringComparer.Ordinal)
            .Select(location => (MetadataReference)AssemblyMetadata.CreateFromFile(location)
                .GetReference(filePath: SharedFrameworkFolder + Path.GetFileName(location))),

        AssemblyMetadata.CreateFromFile(typeof(IServiceCollection).Assembly.Location)
            .GetReference(filePath:
                "/home/test/.nuget/packages/microsoft.extensions.dependencyinjection.abstractions/8.0.0/lib/net8.0/" +
                "Microsoft.Extensions.DependencyInjection.Abstractions.dll"),
    ]);

    public static ImmutableArray<MetadataReference> BaseReferences => FrameworkReferences.Value;

    public static PackageRunResult Run(string source, params ReferencedAssembly[] referencedAssemblies) =>
        Run(CreateCompilation(source, referencedAssemblies));

    public static CSharpCompilation CreateCompilation(string source, params ReferencedAssembly[] referencedAssemblies) =>
        CreateCompilation(
            source,
            BuildReferences(CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13), referencedAssemblies));

    public static CSharpCompilation CreateCompilation(string source, IEnumerable<MetadataReference> references)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13);

        return CSharpCompilation.Create(
            assemblyName: AssemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions, path: "Consumer.cs")],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static PackageRunResult Run(Compilation compilation)
    {
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;

        var generators = new IIncrementalGenerator[]
        {
            new ServicefyByConfigurationGenerator(),
            new ServicefyConventionsGenerator(),
        };

        CSharpGeneratorDriver
            .Create(generators: generators.Select(g => g.AsSourceGenerator()), parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var byBaseType = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs", StringComparison.Ordinal))
            ?.ToString();

        return new PackageRunResult(output, diagnostics, byBaseType) { Input = compilation };
    }

    public static object? RunStaticMethod(
        string source, string typeFullName, string methodName, params ReferencedAssembly[] referencedAssemblies)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13);
        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var references = BuildReferences(parseOptions, referencedAssemblies, images);

        references.Add(AssemblyMetadata.CreateFromFile(typeof(ServiceProvider).Assembly.Location)
            .GetReference(filePath:
                "/home/test/.nuget/packages/microsoft.extensions.dependencyinjection/8.0.0/lib/net8.0/" +
                "Microsoft.Extensions.DependencyInjection.dll"));

        var compilation = CSharpCompilation.Create(
            assemblyName: AssemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions, path: "Consumer.cs")],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generators = new IIncrementalGenerator[]
        {
            new ServicefyByConfigurationGenerator(),
            new ServicefyConventionsGenerator(),
        };

        CSharpGeneratorDriver
            .Create(generators: generators.Select(g => g.AsSourceGenerator()), parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        using var peStream = new MemoryStream();
        var emitResult = output.Emit(peStream);
        if (!emitResult.Success)
            throw new InvalidOperationException(
                "Failed to emit the runtime test assembly:" + Environment.NewLine +
                string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var loadContext = new InMemoryLoadContext(images);
        peStream.Position = 0;
        var assembly = loadContext.LoadFromStream(peStream);

        var type = assembly.GetType(typeFullName)
            ?? throw new InvalidOperationException($"Type '{typeFullName}' not found in the emitted assembly.");
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Static method '{methodName}' not found on '{typeFullName}'.");

        return method.Invoke(null, null);
    }

    private sealed class InMemoryLoadContext(Dictionary<string, byte[]> images)
        : AssemblyLoadContext("ServicefyPackageRuntimeTest", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is null || !images.TryGetValue(assemblyName.Name, out var image)) return null;

            using var stream = new MemoryStream(image);
            return LoadFromStream(stream);
        }
    }

    public static List<MetadataReference> BuildReferences(
        CSharpParseOptions parseOptions,
        IEnumerable<ReferencedAssembly> referencedAssemblies,
        Dictionary<string, byte[]>? images = null)
    {
        var references = new List<MetadataReference>(BaseReferences);

        foreach (var referenced in referencedAssemblies)
        {
            var referencedCompilation = CSharpCompilation.Create(
                assemblyName: referenced.AssemblyName,
                syntaxTrees: [CSharpSyntaxTree.ParseText(referenced.Source, parseOptions)],
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var peStream = new MemoryStream();
            var emitResult = referencedCompilation.Emit(peStream);
            if (!emitResult.Success)
                throw new InvalidOperationException(
                    $"Failed to compile referenced assembly '{referenced.AssemblyName}':" + Environment.NewLine +
                    string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            var image = peStream.ToArray();
            images?.Add(referenced.AssemblyName, image);

            references.Add(AssemblyMetadata
                .CreateFromImage(image.ToImmutableArray())
                .GetReference(filePath: referenced.ResolvedFilePath));
        }

        return references;
    }
}
