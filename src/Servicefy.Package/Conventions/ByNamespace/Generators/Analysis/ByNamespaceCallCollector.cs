using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Conventions.Models;

namespace Servicefy.Package.Conventions.ByNamespace.Generators.Analysis;

/// <summary>
/// Finds <c>.ByNamespace(predicate, lifetime)</c> invocations anywhere in the user's
/// syntax trees and reduces each to a <see cref="NamespaceConventionRule"/>.
/// </summary>
internal static class ByNamespaceCallCollector
{
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ByNamespace" },
            ArgumentList.Arguments.Count: 2
        };

    internal static (NamespaceConventionRule? Rule, Diagnostic? Diagnostic) Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var arguments = invocation.ArgumentList.Arguments;

        if (!NamespacePredicateParser.TryParse(arguments[0].Expression, out var predicate, out var diagnostic) || predicate is null)
            return (null, diagnostic);

        if (!TryGetLifetime(arguments[1].Expression, out var lifetime))
            return (null, null);

        return (new NamespaceConventionRule(arguments[0].Expression.ToString(), lifetime, predicate), null);
    }

    private static bool TryGetLifetime(ExpressionSyntax expr, out string lifetime)
    {
        if (EnumMemberAccessParser.TryGetMember(expr, "Lifetime", out var member)
            && member is "Singleton" or "Scoped" or "Transient")
        {
            lifetime = member;
            return true;
        }

        lifetime = "";
        return false;
    }
}
