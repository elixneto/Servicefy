using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;

namespace Servicefy.Tests.Helpers;

internal static class CompilationHelper
{
    internal static readonly IEnumerable<MetadataReference> BaseReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
    ];

    public static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        string source,
        LanguageVersion langVersion = LanguageVersion.CSharp13)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(langVersion);
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: BaseReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return RunGenerator(compilation, parseOptions);
    }

    public static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGeneratorWithProjectReference(
        string source,
        string referencedSource,
        string referencedAssemblyName = "ReferencedProject",
        LanguageVersion langVersion = LanguageVersion.CSharp13)
    {
        return RunGeneratorWithProjectReferences(
            source, langVersion, (referencedSource, referencedAssemblyName));
    }

    public static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGeneratorWithProjectReferences(
        string source,
        LanguageVersion langVersion = LanguageVersion.CSharp13,
        params (string Source, string AssemblyName)[] referencedProjects)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(langVersion);

        var references = BaseReferences.ToList();

        foreach (var (referencedSource, referencedAssemblyName) in referencedProjects)
            references.Add(CreateProjectReference(referencedSource, referencedAssemblyName, references, parseOptions));

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return RunGenerator(compilation, parseOptions);
    }

    public static PortableExecutableReference CreateProjectReference(
        string source,
        string assemblyName,
        IEnumerable<MetadataReference>? references = null,
        CSharpParseOptions? parseOptions = null)
    {
        parseOptions ??= CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13);

        var referencedCompilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: references ?? BaseReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emitResult = referencedCompilation.Emit(peStream);
        if (!emitResult.Success)
            throw new InvalidOperationException(
                $"Failed to compile referenced project '{assemblyName}': " +
                string.Join(Environment.NewLine, emitResult.Diagnostics));

        return AssemblyMetadata
            .CreateFromImage(peStream.ToArray().ToImmutableArray())
            .GetReference(filePath: $@"E:\fake\{assemblyName}\bin\Debug\net8.0\{assemblyName}.dll");
    }

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        CSharpCompilation compilation, CSharpParseOptions parseOptions)
    {
        var generators = new IIncrementalGenerator[]
        {
            new ServicefyByConfigurationGenerator(),
            new ServicefyConventionsGenerator(),
        };

        CSharpGeneratorDriver
            .Create(generators: generators.Select(g => g.AsSourceGenerator()), parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation, diagnostics);
    }
}
