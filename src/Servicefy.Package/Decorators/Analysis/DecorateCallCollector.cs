using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Servicefy.Package.Decorators.Analysis;

/// <summary>
/// Finds <c>.Decorate&lt;TService, TDecorator&gt;()</c> invocations anywhere in the
/// compilation's syntax trees, resolving both type arguments to symbols.
/// </summary>
internal static class DecorateCallCollector
{
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
