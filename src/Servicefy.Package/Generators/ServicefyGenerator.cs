using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Servicefy.Package.Generators;

[Generator]
public class ServicefyGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, EmitAttributes);

        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Where(c => c is not null);

        context.RegisterSourceOutput(context.CompilationProvider.Combine(classDeclarations.Collect()), ServicefyGeneratorDiagnostics.Emit);
        context.RegisterSourceOutput(context.CompilationProvider, ServicefyGeneratorRegistrations.Emit);
    }
    
    private static void EmitAttributes(SourceProductionContext spc, Compilation compilation)
    {
        var ns = compilation.AssemblyName ?? "Servicefy";
        var emitGeneric = compilation is CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp11 };

        spc.AddSource("Lifetime.g.cs", SourceText.From(LifetimeEnum.Value(ns), Encoding.UTF8));
        spc.AddSource("AddAttribute.g.cs", SourceText.From(AddAttribute.Value(ns, emitGeneric), Encoding.UTF8));
        spc.AddSource("AddScopedAttribute.g.cs", SourceText.From(AddScopedAttribute.Value(ns, emitGeneric), Encoding.UTF8));
        spc.AddSource("AddSingletonAttribute.g.cs", SourceText.From(AddSingletonAttribute.Value(ns, emitGeneric), Encoding.UTF8));
        spc.AddSource("AddTransientAttribute.g.cs", SourceText.From(AddTransientAttribute.Value(ns, emitGeneric), Encoding.UTF8));
        spc.AddSource("ConfigureAttribute.g.cs", SourceText.From(ConfigureAttribute.Value(ns), Encoding.UTF8));
        spc.AddSource("ServicefyAggregatesAttribute.g.cs", SourceText.From(ServicefyAggregatesAttribute.Value(ns), Encoding.UTF8));
    }
}