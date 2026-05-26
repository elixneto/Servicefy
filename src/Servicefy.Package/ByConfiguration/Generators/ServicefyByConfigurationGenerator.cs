using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.ByConfiguration.Generators.Emit;

namespace Servicefy.Package.ByConfiguration.Generators;

/// <summary>
/// Entry point for the Servicefy incremental source generator.
/// Responsible only for wiring the three pipeline stages into the Roslyn
/// <see cref="IncrementalGeneratorInitializationContext"/>; all logic lives in
/// the <c>Emit/</c> classes.
/// </summary>
[Generator]
public sealed class ServicefyByConfigurationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Emit generated attribute source files (Lifetime, AddScoped, AddKeyed*, …)
        context.RegisterSourceOutput(
            context.CompilationProvider,
            AttributeEmitter.Emit);

        // 2. Validate attribute usage and report compile-time diagnostics
        var annotatedClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx,  _) => (ClassDeclarationSyntax)ctx.Node)
            .Where(static c => c is not null);

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(annotatedClasses.Collect()),
            DiagnosticsByConfigurationEmitter.Emit);

        // 3. Collect all service registrations and emit ServicefyExtensions.*.g.cs
        context.RegisterSourceOutput(
            context.CompilationProvider,
            RegistrationByConfigurationEmitter.Emit);
    }
}
