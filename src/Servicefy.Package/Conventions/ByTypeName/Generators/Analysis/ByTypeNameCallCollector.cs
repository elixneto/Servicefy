using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Conventions.Models;

namespace Servicefy.Package.Conventions.ByTypeName.Generators.Analysis;

/// <summary>
/// Finds <c>.ByTypeName(predicate, lifetime)</c> invocations anywhere in the user's syntax
/// trees and reduces each to a <see cref="TypeNameConventionRule"/>.
/// </summary>
internal static class ByTypeNameCallCollector
{
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: "ByTypeName" } },
            ArgumentList.Arguments.Count: 2
        };

    internal static (TypeNameConventionRule? Rule, Diagnostic? Diagnostic) Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var arguments = invocation.ArgumentList.Arguments;

        if (!TypeNamePredicateParser.TryParse(arguments[0].Expression, out var predicate, out var diagnostic) || predicate is null)
            return (null, diagnostic);

        if (!EnumMemberAccessParser.TryGetMember(arguments[1].Expression, "Lifetime", out var lifetime)
            || lifetime is not ("Singleton" or "Scoped" or "Transient"))
            return (null, null);

        return (new TypeNameConventionRule(arguments[0].Expression.ToString(), lifetime, predicate), null);
    }
}
