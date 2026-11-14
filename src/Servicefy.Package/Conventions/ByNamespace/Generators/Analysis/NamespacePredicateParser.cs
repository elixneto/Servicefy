using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Diagnostics;

namespace Servicefy.Package.Conventions.ByNamespace.Generators.Analysis;

/// <summary>
/// Interprets the syntax of a <c>Func&lt;string, bool&gt;</c> lambda passed to
/// <c>ByNamespace(...)</c> into a pure <see cref="Func{T,TResult}"/> that can be
/// evaluated against namespace names at generation time.
/// </summary>
/// <remarks>
/// Supports expression-bodied lambdas built from: <c>StartsWith</c>, <c>EndsWith</c>,
/// <c>Contains</c>, <c>Equals</c> (optionally with a <c>StringComparison</c> argument),
/// <c>==</c> / <c>!=</c> against a string literal, <c>!</c>, <c>&amp;&amp;</c>, <c>||</c> and parentheses.
/// <c>StartsWith</c>, <c>EndsWith</c> and <c>Contains</c> also accept an array/collection of
/// string literals (e.g. <c>nm.EndsWith(["Repository", "Handler"])</c>), matching if any element matches.
/// The literal <c>true</c> is also supported as a body (e.g. <c>_ => true</c>), matching everything.
/// The literal <c>false</c> is not supported — it would match nothing.
/// </remarks>
internal static class NamespacePredicateParser
{
    /// <summary>
    /// Re-parses a predicate from its source text (as kept in a value-equatable rule model).
    /// Diagnostics were already reported when the call site was collected, so they are dropped here.
    /// </summary>
    internal static Func<string, bool>? ParseText(string predicateExpression) =>
        TryParse(SyntaxFactory.ParseExpression(predicateExpression), out var predicate, out _) ? predicate : null;

    /// <summary>
    /// Namespace prefixes every namespace matched by the predicate is guaranteed to start with, when
    /// that can be read off its shape: <c>StartsWith</c> with literals (any of its forms), <c>==</c> /
    /// <c>Equals</c> with a literal, <c>&amp;&amp;</c> (either side), <c>||</c> (both sides, same
    /// comparison) and parentheses. Used to prune which namespaces of a referenced assembly are walked
    /// at all under <c>ConventionScope.AllReferences</c>. False when no such guarantee exists
    /// (<c>EndsWith</c>, <c>Contains</c>, <c>!</c>, <c>_ =&gt; true</c>, ...).
    /// </summary>
    internal static bool TryGetRequiredPrefixes(
        string predicateExpression, out IReadOnlyList<string> prefixes, out StringComparison comparison)
    {
        var found = TryGetRequiredBounds(predicateExpression, out var bounds, out comparison);
        prefixes = bounds.Select(b => b.Value).ToList();
        return found;
    }

    /// <summary>
    /// The open-ended <c>StartsWith</c> prefixes among <see cref="TryGetRequiredPrefixes"/>' bounds, i.e.
    /// without the exact <c>==</c> / <c>Equals</c> values, which match a single namespace however short
    /// they are. Used for the SVCFY017 "very short prefix" check.
    /// </summary>
    internal static bool TryGetRequiredOpenPrefixes(string predicateExpression, out IReadOnlyList<string> prefixes)
    {
        var found = TryGetRequiredBounds(predicateExpression, out var bounds, out _);
        prefixes = bounds.Where(b => !b.Exact).Select(b => b.Value).ToList();
        return found;
    }

    private static bool TryGetRequiredBounds(
        string predicateExpression, out IReadOnlyList<(string Value, bool Exact)> bounds, out StringComparison comparison)
    {
        bounds = [];
        comparison = StringComparison.Ordinal;

        (string? parameterName, ExpressionSyntax? body) = SyntaxFactory.ParseExpression(predicateExpression) switch
        {
            SimpleLambdaExpressionSyntax simple => (simple.Parameter.Identifier.ValueText, simple.ExpressionBody),
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } paren =>
                (paren.ParameterList.Parameters[0].Identifier.ValueText, paren.ExpressionBody),
            _ => (null, null)
        };

        if (parameterName is null || body is null) return false;

        var result = GetRequiredPrefixes(body, parameterName);
        if (result is null) return false;

        (bounds, comparison) = result.Value;
        return true;
    }

    private static (IReadOnlyList<(string Value, bool Exact)> Prefixes, StringComparison Comparison)? GetRequiredPrefixes(ExpressionSyntax expr, string parameterName)
    {
        switch (expr)
        {
            case ParenthesizedExpressionSyntax paren:
                return GetRequiredPrefixes(paren.Expression, parameterName);

            // Both sides must hold, so either side's prefixes are a valid bound.
            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.LogicalAndExpression):
                return GetRequiredPrefixes(bin.Left, parameterName) ?? GetRequiredPrefixes(bin.Right, parameterName);

            // Either side may hold, so both must be bounded (and comparable).
            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.LogicalOrExpression):
                var left = GetRequiredPrefixes(bin.Left, parameterName);
                var right = GetRequiredPrefixes(bin.Right, parameterName);
                return left is not null && right is not null && left.Value.Comparison == right.Value.Comparison
                    ? ([.. left.Value.Prefixes, .. right.Value.Prefixes], left.Value.Comparison)
                    : null;

            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.EqualsExpression):
                return TryGetParameterAndLiteral(bin.Left, bin.Right, parameterName, out var equalTo)
                    ? ([(equalTo, true)], StringComparison.Ordinal)
                    : null;

            case InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } mae
            } invocation when id.Identifier.ValueText == parameterName:
                return GetMethodPrefixes(mae.Name.Identifier.ValueText, invocation.ArgumentList.Arguments);

            default:
                return null;
        }
    }

    private static (IReadOnlyList<(string Value, bool Exact)> Prefixes, StringComparison Comparison)? GetMethodPrefixes(
        string methodName, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (methodName is not ("StartsWith" or "Equals") || arguments.Count == 0) return null;

        var expressions = arguments.Select(a => a.Expression).ToList();
        var comparison = StringComparison.Ordinal;

        // Method(StringComparison.X, "a", "b", ...) or Method(..., StringComparison.X)
        if (TryGetStringComparison(expressions[0], out var leading))
        {
            comparison = leading;
            expressions.RemoveAt(0);
        }
        else if (expressions.Count > 1 && TryGetStringComparison(expressions[^1], out var trailing))
        {
            comparison = trailing;
            expressions.RemoveAt(expressions.Count - 1);
        }

        if (expressions.Count == 0) return null;

        // Method([...]) / Method([...], StringComparison.X)
        if (expressions.Count == 1
            && ArrayLiteralParser.TryGetStringElements(expressions[0], out var arrayValues, out var invalidElement))
            return invalidElement is null && arrayValues is { Count: > 0 }
                ? (arrayValues.Select(v => (v, methodName == "Equals")).ToList(), comparison)
                : null;

        var values = new List<string>();
        foreach (var expression in expressions)
        {
            if (!TryGetStringLiteral(expression, out var value)) return null;
            values.Add(value);
        }

        // Equals takes a single value; several literals would not compile as Equals anyway.
        return methodName == "Equals" && values.Count != 1
            ? null
            : (values.Select(v => (v, methodName == "Equals")).ToList(), comparison);
    }

    internal static bool TryParse(ExpressionSyntax argumentExpression, out Func<string, bool>? predicate, out Diagnostic? diagnostic)
    {
        predicate = null;
        diagnostic = null;

        (string? parameterName, ExpressionSyntax? body) = argumentExpression switch
        {
            SimpleLambdaExpressionSyntax simple => (simple.Parameter.Identifier.ValueText, simple.ExpressionBody),
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } paren =>
                (paren.ParameterList.Parameters[0].Identifier.ValueText, paren.ExpressionBody),
            _ => (null, null)
        };

        if (parameterName is null || body is null)
        {
            diagnostic = new SVCFY012(argumentExpression.GetLocation()).CreateDiagnostic();
            return false;
        }

        if (TryBuildEvaluator(body, parameterName, out predicate, out diagnostic) && predicate is not null)
            return true;

        diagnostic ??= new SVCFY012(body.GetLocation()).CreateDiagnostic();
        return false;
    }

    private static bool TryBuildEvaluator(ExpressionSyntax expr, string parameterName, out Func<string, bool>? evaluator, out Diagnostic? diagnostic)
    {
        diagnostic = null;

        switch (expr)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                evaluator = _ => true;
                return true;

            case ParenthesizedExpressionSyntax paren:
                return TryBuildEvaluator(paren.Expression, parameterName, out evaluator, out diagnostic);

            case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression):
                if (!TryBuildEvaluator(unary.Operand, parameterName, out var negated, out diagnostic) || negated is null)
                {
                    evaluator = null;
                    return false;
                }

                evaluator = value => !negated(value);
                return true;

            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.LogicalAndExpression):
                return TryBuildLogical(bin, parameterName, isAnd: true, out evaluator, out diagnostic);

            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.LogicalOrExpression):
                return TryBuildLogical(bin, parameterName, isAnd: false, out evaluator, out diagnostic);

            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.EqualsExpression) || bin.IsKind(SyntaxKind.NotEqualsExpression):
                return TryBuildEquality(bin, parameterName, out evaluator);

            case InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } mae
            } invocation when id.Identifier.ValueText == parameterName:
                return TryBuildStringMethod(mae.Name.Identifier.ValueText, invocation.ArgumentList.Arguments, out evaluator, out diagnostic);

            default:
                evaluator = null;
                diagnostic = new SVCFY012(expr.GetLocation()).CreateDiagnostic();
                return false;
        }
    }

    private static bool TryBuildLogical(BinaryExpressionSyntax bin, string parameterName, bool isAnd, out Func<string, bool>? evaluator, out Diagnostic? diagnostic)
    {
        if (!TryBuildEvaluator(bin.Left, parameterName, out var left, out diagnostic) || left is null)
        {
            evaluator = null;
            return false;
        }

        if (!TryBuildEvaluator(bin.Right, parameterName, out var right, out diagnostic) || right is null)
        {
            evaluator = null;
            return false;
        }

        evaluator = isAnd
            ? value => left(value) && right(value)
            : value => left(value) || right(value);
        return true;
    }

    private static bool TryBuildEquality(BinaryExpressionSyntax bin, string parameterName, out Func<string, bool>? evaluator)
    {
        evaluator = null;

        if (!TryGetParameterAndLiteral(bin.Left, bin.Right, parameterName, out var literal))
            return false;

        var expectEqual = bin.IsKind(SyntaxKind.EqualsExpression);
        evaluator = value => (value == literal) == expectEqual;
        return true;
    }

    private static bool TryGetParameterAndLiteral(ExpressionSyntax left, ExpressionSyntax right, string parameterName, out string literal)
    {
        if (IsParameter(left, parameterName) && TryGetStringLiteral(right, out literal)) return true;
        if (IsParameter(right, parameterName) && TryGetStringLiteral(left, out literal)) return true;

        literal = "";
        return false;
    }

    private static bool TryBuildStringMethod(
        string methodName,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        out Func<string, bool>? evaluator,
        out Diagnostic? diagnostic)
    {
        evaluator = null;
        diagnostic = null;

        if (arguments.Count == 0) return false;

        var first = arguments[0].Expression;

        // Method(StringComparison.X, "a", "b", ...) — params form with a leading comparison.
        if (TryGetStringComparison(first, out var leadingComparison))
        {
            if (arguments.Count < 2) return false;

            if (!TryGetAllStringLiterals(arguments.Skip(1), methodName, out var leadingValues, out diagnostic) || leadingValues is null)
                return false;

            evaluator = BuildMulti(methodName, leadingValues, leadingComparison);
            return evaluator is not null;
        }

        if (TryGetStringLiteral(first, out var singleValue))
        {
            // Method("value")
            if (arguments.Count == 1)
            {
                evaluator = BuildSingle(methodName, singleValue, StringComparison.Ordinal);
                return evaluator is not null;
            }

            // Method("value", StringComparison.X)
            if (arguments.Count == 2 && TryGetStringComparison(arguments[1].Expression, out var trailingComparison))
            {
                evaluator = BuildSingle(methodName, singleValue, trailingComparison);
                return evaluator is not null;
            }

            // Method("a", "b", ...) — params form, Ordinal default.
            if (!TryGetAllStringLiterals(arguments, methodName, out var paramsValues, out diagnostic) || paramsValues is null)
                return false;

            evaluator = BuildMulti(methodName, paramsValues, StringComparison.Ordinal);
            return evaluator is not null;
        }

        // Method([...]) or Method([...], StringComparison.X)
        if (ArrayLiteralParser.TryGetStringElements(first, out var arrayValues, out var invalidElement))
        {
            if (invalidElement is not null)
            {
                diagnostic = new SVCFY011(invalidElement.GetLocation()).CreateDiagnostic(methodName);
                return false;
            }

            var comparison = arguments.Count > 1 && TryGetStringComparison(arguments[1].Expression, out var arrayComparison)
                ? arrayComparison
                : StringComparison.Ordinal;

            evaluator = BuildMulti(methodName, arrayValues!, comparison);
            return evaluator is not null;
        }

        return false;
    }

    private static bool TryGetAllStringLiterals(IEnumerable<ArgumentSyntax> arguments, string methodName, out List<string>? values, out Diagnostic? diagnostic)
    {
        diagnostic = null;
        var result = new List<string>();

        foreach (var argument in arguments)
        {
            if (TryGetStringLiteral(argument.Expression, out var value))
            {
                result.Add(value);
                continue;
            }

            values = null;

            if (methodName is "StartsWith" or "EndsWith" or "Contains")
                diagnostic = new SVCFY011(argument.Expression.GetLocation()).CreateDiagnostic(methodName);

            return false;
        }

        values = result;
        return true;
    }

    private static Func<string, bool>? BuildSingle(string methodName, string value, StringComparison comparison) => methodName switch
    {
        "StartsWith" => ns => ns.StartsWith(value, comparison),
        "EndsWith" => ns => ns.EndsWith(value, comparison),
        "Contains" => ns => ns.Contains(value, comparison),
        "Equals" => ns => ns.Equals(value, comparison),
        _ => null
    };

    private static Func<string, bool>? BuildMulti(string methodName, List<string> values, StringComparison comparison) => methodName switch
    {
        "StartsWith" => ns => values.Any(value => ns.StartsWith(value, comparison)),
        "EndsWith" => ns => values.Any(value => ns.EndsWith(value, comparison)),
        "Contains" => ns => values.Any(value => ns.Contains(value, comparison)),
        _ => null
    };

    private static bool IsParameter(ExpressionSyntax expr, string parameterName) =>
        expr is IdentifierNameSyntax id && id.Identifier.ValueText == parameterName;

    private static bool TryGetStringLiteral(ExpressionSyntax expr, out string value)
    {
        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            value = literal.Token.ValueText;
            return true;
        }

        value = "";
        return false;
    }

    private static bool TryGetStringComparison(ExpressionSyntax expr, out StringComparison comparison)
    {
        comparison = StringComparison.Ordinal;

        return expr is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "StringComparison" }
            } mae
            && Enum.TryParse(mae.Name.Identifier.ValueText, out comparison);
    }
}
