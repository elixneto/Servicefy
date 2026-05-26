using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;

namespace Servicefy.Tests.Helpers;

internal static class CompilationHelper
{
    private static readonly IEnumerable<MetadataReference> BaseReferences =
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

    /// <summary>
    /// Compiles <paramref name="referencedSource"/> into a separate assembly and references it from
    /// the main compilation as if it were a sibling project (<c>&lt;ProjectReference&gt;</c>) — i.e.
    /// a <see cref="PortableExecutableReference"/> whose path is outside the NuGet packages folder.
    /// </summary>
    public static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGeneratorWithProjectReference(
        string source,
        string referencedSource,
        string referencedAssemblyName = "ReferencedProject",
        LanguageVersion langVersion = LanguageVersion.CSharp13)
    {
        return RunGeneratorWithProjectReferences(
            source, langVersion, (referencedSource, referencedAssemblyName));
    }

    /// <summary>
    /// Compiles a chain of <paramref name="referencedProjects"/> (each one can see the types of every
    /// project compiled before it, like a real project-reference graph) and references all of them
    /// from the main compilation — as if they were sibling projects (<c>&lt;ProjectReference&gt;</c>),
    /// i.e. <see cref="PortableExecutableReference"/>s whose paths are outside the NuGet packages folder.
    /// MSBuild flattens transitive project references into direct references, so the main compilation
    /// gets all of them, matching what a real build would see.
    /// </summary>
    public static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGeneratorWithProjectReferences(
        string source,
        LanguageVersion langVersion = LanguageVersion.CSharp13,
        params (string Source, string AssemblyName)[] referencedProjects)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(langVersion);

        var references = BaseReferences.ToList();

        foreach (var (referencedSource, referencedAssemblyName) in referencedProjects)
        {
            var referencedCompilation = CSharpCompilation.Create(
                assemblyName: referencedAssemblyName,
                syntaxTrees: [CSharpSyntaxTree.ParseText(referencedSource, parseOptions)],
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var peStream = new MemoryStream();
            var emitResult = referencedCompilation.Emit(peStream);
            if (!emitResult.Success)
                throw new InvalidOperationException(
                    $"Failed to compile referenced project '{referencedAssemblyName}': " +
                    string.Join(Environment.NewLine, emitResult.Diagnostics));

            var projectReference = AssemblyMetadata
                .CreateFromImage(peStream.ToArray().ToImmutableArray())
                .GetReference(filePath: $@"E:\fake\{referencedAssemblyName}\bin\Debug\net8.0\{referencedAssemblyName}.dll");

            references.Add(projectReference);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return RunGenerator(compilation, parseOptions);
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
