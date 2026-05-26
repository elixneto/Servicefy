using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Servicefy.Package.Generators;

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

        var generator = new ServicefyGenerator();
        CSharpGeneratorDriver
            .Create(generators: [generator.AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation, diagnostics);
    }
}
