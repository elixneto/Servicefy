using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Servicefy.Package.ByConfiguration.Generators.Emit;

/// <summary>
/// Emits the generated source files for all Servicefy attributes and the <c>Lifetime</c> enum.
/// Called once per compilation — output is deterministic and independent of user code.
/// </summary>
internal static class AttributeEmitter
{
    internal static void Emit(SourceProductionContext spc, Compilation compilation)
    {
        var ns = compilation.AssemblyName ?? "Servicefy";

        Add(spc, "Lifetime.g.cs",                    LifetimeEnum.Value(ns));
        Add(spc, "AddAttribute.g.cs",                AddAttribute.Value(ns));
        Add(spc, "AddScopedAttribute.g.cs",           AddScopedAttribute.Value(ns));
        Add(spc, "AddSingletonAttribute.g.cs",        AddSingletonAttribute.Value(ns));
        Add(spc, "AddTransientAttribute.g.cs",        AddTransientAttribute.Value(ns));
        Add(spc, "AddKeyedScopedAttribute.g.cs",      AddKeyedScopedAttribute.Value(ns));
        Add(spc, "AddKeyedSingletonAttribute.g.cs",   AddKeyedSingletonAttribute.Value(ns));
        Add(spc, "AddKeyedTransientAttribute.g.cs",   AddKeyedTransientAttribute.Value(ns));
        Add(spc, "AddSelfAttribute.g.cs",             AddSelfAttribute.Value(ns));
        Add(spc, "AddSelfScopedAttribute.g.cs",       AddSelfScopedAttribute.Value(ns));
        Add(spc, "AddSelfSingletonAttribute.g.cs",    AddSelfSingletonAttribute.Value(ns));
        Add(spc, "AddSelfTransientAttribute.g.cs",    AddSelfTransientAttribute.Value(ns));
        Add(spc, "ConfigureAttribute.g.cs",           ConfigureAttribute.Value(ns));
        Add(spc, "DecoratorForAttribute.g.cs",        Decorators.DecoratorForAttribute.Value(ns));
        Add(spc, "DecoratorAttribute.g.cs",           Decorators.DecoratorAttribute.Value(ns));
        Add(spc, "ServicefyDecorateExtensions.g.cs",  Decorators.DecorateExtensionsTemplate.Value(ns));
        Add(spc, "ServicefyAggregatesAttribute.g.cs", ServicefyAggregatesAttribute.Value(ns));
    }

    private static void Add(SourceProductionContext spc, string hintName, string source) =>
        spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
}
