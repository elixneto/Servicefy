using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;

namespace Servicefy.Tests.Helpers;

internal static class RuntimeCompilationHelper
{
    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var refs = tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        refs.Add(MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(ServiceProvider).Assembly.Location));
        return refs;
    }

    public static object? RunStaticMethod(
        string source,
        string typeFullName,
        string methodName,
        string rootNamespace = "TestAssembly",
        LanguageVersion langVersion = LanguageVersion.CSharp13)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(langVersion);

        var compilation = CSharpCompilation.Create(
            assemblyName: rootNamespace,
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generators = new IIncrementalGenerator[]
        {
            new ServicefyByConfigurationGenerator(),
            new ServicefyConventionsGenerator(),
        };

        CSharpGeneratorDriver
            .Create(generators: generators.Select(g => g.AsSourceGenerator()), parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        using var peStream = new MemoryStream();
        var emitResult = outputCompilation.Emit(peStream);
        if (!emitResult.Success)
            throw new InvalidOperationException(
                "Failed to emit the runtime test assembly:" + Environment.NewLine +
                string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        peStream.Position = 0;
        var loadContext = new AssemblyLoadContext("ServicefyRuntimeTest", isCollectible: true);
        var assembly = loadContext.LoadFromStream(peStream);

        var type = assembly.GetType(typeFullName)
            ?? throw new InvalidOperationException($"Type '{typeFullName}' not found in the emitted assembly.");

        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Static method '{methodName}' not found on '{typeFullName}'.");

        return method.Invoke(null, null);
    }
}
