using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Servicefy.Package.Conventions.Generators.Analysis;

/// <summary>
/// Shared helper for parsing array/collection-expression literals of string literals — used by the
/// array overloads of <c>StartsWith</c>/<c>EndsWith</c>/<c>Contains</c> in convention predicates
/// (e.g. <c>name.EndsWith(["Repository", "Handler"])</c> or <c>name.EndsWith(new[] { "Repository" })</c>).
/// </summary>
internal static class ArrayLiteralParser
{
    /// <summary>
    /// Returns <c>false</c> if <paramref name="expr"/> isn't array/collection-expression syntax at all
    /// (the caller should fall back to its own handling, e.g. treat it as an unsupported argument).
    /// Otherwise returns <c>true</c>: either <paramref name="values"/> is populated (every element is a
    /// string literal) or <paramref name="invalidElement"/> is set to the first element that isn't.
    /// </summary>
    internal static bool TryGetStringElements(ExpressionSyntax expr, out List<string>? values, out SyntaxNode? invalidElement)
    {
        switch (expr)
        {
            case CollectionExpressionSyntax collection:
                return TryExtract(collection.Elements.Select(ElementNode), out values, out invalidElement);

            case ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer }:
                return TryExtract(initializer.Expressions, out values, out invalidElement);

            case ArrayCreationExpressionSyntax { Initializer: { } initializer }:
                return TryExtract(initializer.Expressions, out values, out invalidElement);

            default:
                values = null;
                invalidElement = null;
                return false;
        }
    }

    private static SyntaxNode ElementNode(CollectionElementSyntax element) =>
        element is ExpressionElementSyntax { Expression: { } expr } ? expr : element;

    private static bool TryExtract(IEnumerable<SyntaxNode> elements, out List<string>? values, out SyntaxNode? invalidElement)
    {
        var collected = new List<string>();

        foreach (var element in elements)
        {
            if (element is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                values = null;
                invalidElement = element;
                return true;
            }

            collected.Add(literal.Token.ValueText);
        }

        values = collected;
        invalidElement = null;
        return true;
    }
}
