using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Servicefy.Package.Conventions.ByNamespace.Generators.Emit;

/// <summary>
/// Emits the <c>ServicefyConventionsBuilder</c> runtime API (<see cref="ServicefyConventionsBuilderTemplate"/>).
/// Called once per compilation — output is deterministic and independent of user code.
/// </summary>
internal static class ConventionsBuilderEmitter
{
    internal static void Emit(SourceProductionContext spc, Compilation compilation)
    {
        var ns = compilation.AssemblyName ?? "Servicefy";

        spc.AddSource(
            "ServicefyConventionsBuilder.g.cs",
            SourceText.From(ServicefyConventionsBuilderTemplate.Value(ns), Encoding.UTF8));

        spc.AddSource(
            "ServiceTypeSelector.g.cs",
            SourceText.From(ServiceTypeSelectorEnum.Value(ns), Encoding.UTF8));
    }
}
