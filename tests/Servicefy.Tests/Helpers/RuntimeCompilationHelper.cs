using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;

namespace Servicefy.Tests.Helpers;

/// <summary>
/// Runs the Servicefy generators over <paramref name="source"/>, emits the resulting compilation
/// (user source + generated registrations) to a real assembly, loads it, and invokes a static
/// method on it. Unlike <see cref="CompilationHelper"/> — which only inspects generated text — this
/// actually executes the generated DI registrations against Microsoft.Extensions.DependencyInjection,
/// so a test can build a ServiceProvider and assert on what it resolves.
/// </summary>
internal static class RuntimeCompilationHelper
{
    // The full BCL surface plus the exact Microsoft.Extensions.DependencyInjection assemblies the
    // test process already loaded — so the emitted assembly binds to the same IServiceCollection /
    // ServiceProvider types and resolution behaves identically to a real app.
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

    /// <summary>
    /// Compiles and loads <paramref name="source"/>, then invokes the static, parameterless method
    /// <paramref name="typeFullName"/>.<paramref name="methodName"/> and returns its result.
    /// </summary>
    /// <param name="rootNamespace">
    /// The assembly name to compile under. Servicefy emits its runtime API (Lifetime,
    /// AddServicefyConventions, the builder) into the assembly-name namespace, so the source's
    /// own namespace must be this value or a sub-namespace of it for those types to be in scope.
    /// </param>
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
        // A dedicated context per call so multiple runtime tests can compile under the same simple
        // assembly name without colliding; dependencies (Microsoft.Extensions.DependencyInjection)
        // fall back to the default context, preserving type identity with the test process.
        var loadContext = new AssemblyLoadContext("ServicefyRuntimeTest", isCollectible: true);
        var assembly = loadContext.LoadFromStream(peStream);

        var type = assembly.GetType(typeFullName)
            ?? throw new InvalidOperationException($"Type '{typeFullName}' not found in the emitted assembly.");

        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Static method '{methodName}' not found on '{typeFullName}'.");

        return method.Invoke(null, null);
    }
}
