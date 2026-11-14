using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Conventions.Models;
using Servicefy.Package.Diagnostics;
using Servicefy.Package.Pipeline;

namespace Servicefy.Package.Conventions.ByNamespace.Generators.Analysis;

/// <summary>
/// Finds <c>.ByNamespace(predicate, lifetime[, scope])</c> invocations anywhere in the user's
/// syntax trees and reduces each to a <see cref="NamespaceConventionRule"/>.
/// </summary>
internal static class ByNamespaceCallCollector
{
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ByNamespace" },
            ArgumentList.Arguments.Count: 2 or 3
        };

    // Shortest StartsWith prefix not considered "too broad" under ConventionScope.AllReferences (SVCFY017).
    internal const int MinAllReferencesPrefixLength = 4;

    internal static (NamespaceConventionRule? Rule, DiagnosticInfo? Diagnostic) Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var arguments = invocation.ArgumentList.Arguments;

        if (!NamespacePredicateParser.TryParse(arguments[0].Expression, out var predicate, out var diagnostic) || predicate is null)
            return (null, diagnostic is null ? null : DiagnosticInfo.From(diagnostic));

        if (!TryGetLifetime(arguments[1].Expression, out var lifetime))
            return (null, null);

        if (!ConventionScopeParser.TryGetScope(arguments, out var scope))
            return (null, null);

        var predicateExpression = arguments[0].Expression.ToString();
        var rule = new NamespaceConventionRule(predicateExpression, lifetime, scope);

        // The rule is still applied; the warning only flags a scan of every package for little benefit.
        return scope == ConventionScopes.AllReferences && IsTooBroad(arguments[0].Expression, predicateExpression)
            ? (rule, DiagnosticInfo.From(new SVCFY017(arguments[0].GetLocation()).CreateDiagnostic(predicateExpression)))
            : (rule, null);
    }

    // `_ => true`, or a StartsWith bound whose shortest prefix is only a few characters long (an exact
    // `ns == "App"` match is narrow however short it is).
    private static bool IsTooBroad(ExpressionSyntax predicate, string predicateExpression) =>
        ConventionScopeParser.IsMatchAll(predicate)
        || (NamespacePredicateParser.TryGetRequiredOpenPrefixes(predicateExpression, out var prefixes)
            && prefixes.Any(p => p.Length < MinAllReferencesPrefixLength));

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
