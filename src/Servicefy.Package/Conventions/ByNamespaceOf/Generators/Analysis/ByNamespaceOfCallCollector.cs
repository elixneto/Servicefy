using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.ByNamespace.Generators.Analysis;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Conventions.Models;

namespace Servicefy.Package.Conventions.ByNamespaceOf.Generators.Analysis;

/// <summary>
/// Finds <c>.ByNamespaceOf&lt;TMarker&gt;(predicate, lifetime)</c> invocations anywhere in the
/// user's syntax trees and reduces each to a <see cref="NamespaceOfConventionRule"/>.
/// </summary>
internal static class ByNamespaceOfCallCollector
{
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    Identifier.ValueText: "ByNamespaceOf",
                    TypeArgumentList.Arguments.Count: 1
                }
            },
            ArgumentList.Arguments.Count: 2
        };

    internal static (NamespaceOfConventionRule? Rule, Diagnostic? Diagnostic) Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var genericName = (GenericNameSyntax)memberAccess.Name;
        var arguments = invocation.ArgumentList.Arguments;
        var semanticModel = context.SemanticModel;

        var markerTypeSyntax = genericName.TypeArgumentList.Arguments[0];
        if (semanticModel.GetTypeInfo(markerTypeSyntax).Type is not INamedTypeSymbol markerType)
            return (null, null);

        if (!NamespacePredicateParser.TryParse(arguments[0].Expression, out var predicate, out var diagnostic) || predicate is null)
            return (null, diagnostic);

        if (!EnumMemberAccessParser.TryGetMember(arguments[1].Expression, "Lifetime", out var lifetime)
            || lifetime is not ("Singleton" or "Scoped" or "Transient"))
            return (null, null);

        return (new NamespaceOfConventionRule(
            markerType,
            markerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            markerType.ContainingNamespace.ToDisplayString(),
            arguments[0].Expression.ToString(),
            lifetime,
            predicate), null);
    }
}
