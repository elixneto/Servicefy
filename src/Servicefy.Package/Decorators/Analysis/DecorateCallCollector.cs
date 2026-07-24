using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Servicefy.Package.Decorators.Analysis;

/// <summary>
/// Finds <c>.Decorate&lt;TService, TDecorator&gt;()</c> invocations anywhere in the
/// compilation's syntax trees, resolving both type arguments to symbols.
/// </summary>
internal static class DecorateCallCollector
{
    /// <summary>
    /// Finds <c>.Decorate(typeof(IFoo&lt;&gt;), typeof(FooDecorator&lt;&gt;))</c> invocations, resolving
    /// both <c>typeof</c> arguments to their unbound generic definitions. Only unbound generic types
    /// are returned; closed/non-generic arguments are ignored (they belong to the generic overload).
    /// </summary>
    internal static List<(INamedTypeSymbol UnboundService, INamedTypeSymbol UnboundDecorator, Location Location)> CollectOpenGeneric(
        Compilation compilation)
    {
        var results = new List<(INamedTypeSymbol, INamedTypeSymbol, Location)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);

            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax
                    {
                        Name: IdentifierNameSyntax { Identifier.ValueText: "Decorate" }
                    }
                    || invocation.ArgumentList.Arguments.Count != 2
                    || invocation.ArgumentList.Arguments[0].Expression is not TypeOfExpressionSyntax serviceTypeOf
                    || invocation.ArgumentList.Arguments[1].Expression is not TypeOfExpressionSyntax decoratorTypeOf)
                {
                    continue;
                }

                if (semanticModel.GetTypeInfo(serviceTypeOf.Type).Type is not INamedTypeSymbol { IsUnboundGenericType: true } service
                    || semanticModel.GetTypeInfo(decoratorTypeOf.Type).Type is not INamedTypeSymbol { IsUnboundGenericType: true } decorator)
                {
                    continue;
                }

                results.Add((service.OriginalDefinition, decorator.OriginalDefinition, invocation.GetLocation()));
            }
        }

        return results;
    }

    internal static List<(INamedTypeSymbol Service, INamedTypeSymbol Decorator, Location Location)> Collect(
        Compilation compilation)
    {
        var results = new List<(INamedTypeSymbol Service, INamedTypeSymbol Decorator, Location Location)>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);

            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax
                    {
                        Name: GenericNameSyntax
                        {
                            Identifier.ValueText: "Decorate",
                            TypeArgumentList.Arguments.Count: 2
                        } generic
                    })
                {
                    continue;
                }

                var serviceType = semanticModel.GetTypeInfo(generic.TypeArgumentList.Arguments[0]).Type as INamedTypeSymbol;
                var decoratorType = semanticModel.GetTypeInfo(generic.TypeArgumentList.Arguments[1]).Type as INamedTypeSymbol;

                if (serviceType is null || decoratorType is null) continue;

                results.Add((serviceType, decoratorType, invocation.GetLocation()));
            }
        }

        return results;
    }
}
