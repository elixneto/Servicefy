using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Conventions.Models;
using Servicefy.Package.Diagnostics;
using Servicefy.Package.Pipeline;

namespace Servicefy.Package.Conventions.ByTypeName.Generators.Analysis;

/// <summary>
/// Finds <c>.ByTypeName(predicate, lifetime[, scope])</c> invocations anywhere in the user's syntax
/// trees and reduces each to a <see cref="TypeNameConventionRule"/>.
/// </summary>
internal static class ByTypeNameCallCollector
{
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: "ByTypeName" } },
            ArgumentList.Arguments.Count: 2 or 3
        };

    internal static (TypeNameConventionRule? Rule, DiagnosticInfo? Diagnostic) Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var arguments = invocation.ArgumentList.Arguments;

        if (!TypeNamePredicateParser.TryParse(arguments[0].Expression, out var predicate, out var diagnostic) || predicate is null)
            return (null, diagnostic is null ? null : DiagnosticInfo.From(diagnostic));

        if (!EnumMemberAccessParser.TryGetMember(arguments[1].Expression, "Lifetime", out var lifetime)
            || lifetime is not ("Singleton" or "Scoped" or "Transient"))
            return (null, null);

        if (!ConventionScopeParser.TryGetScope(arguments, out var scope))
            return (null, null);

        var predicateExpression = arguments[0].Expression.ToString();
        var rule = new TypeNameConventionRule(predicateExpression, lifetime, scope);

        // The rule is still applied; the warning only flags a scan of every package for little benefit.
        return scope == ConventionScopes.AllReferences && ConventionScopeParser.IsMatchAll(arguments[0].Expression)
            ? (rule, DiagnosticInfo.From(new SVCFY017(arguments[0].GetLocation()).CreateDiagnostic(predicateExpression)))
            : (rule, null);
    }
}
