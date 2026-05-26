using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Diagnostics;

namespace Servicefy.Package.Conventions.ByTypeName.Generators.Analysis;

/// <summary>
/// Interprets the syntax of a <c>Func&lt;string, string, bool&gt;</c> lambda passed to
/// <c>ByTypeName(...)</c> into a pure <see cref="Func{T1,T2,TResult}"/> that can be evaluated
/// against (namespace, type name) pairs at generation time.
/// </summary>
/// <remarks>
/// Supports expression-bodied lambdas with two parameters <c>(ns, name) =&gt; ...</c>, built from:
/// <c>StartsWith</c>, <c>EndsWith</c>, <c>Contains</c>, <c>Equals</c> (optionally with a
/// <c>StringComparison</c> argument), <c>==</c> / <c>!=</c> against a string literal, <c>!</c>,
/// <c>&amp;&amp;</c>, <c>||</c> and parentheses — applied to either parameter.
/// <c>StartsWith</c>, <c>EndsWith</c> and <c>Contains</c> also accept an array/collection of
/// string literals (e.g. <c>name.EndsWith(["Repository", "Handler"])</c>), matching if any element matches.
/// The literal <c>true</c> is also supported as a body (e.g. <c>(_, _) => true</c>), matching everything.
/// The literal <c>false</c> is not supported — it would match nothing.
/// </remarks>
internal static class TypeNamePredicateParser
{
    internal static bool TryParse(ExpressionSyntax argumentExpression, out Func<string, string, bool>? predicate, out Diagnostic? diagnostic)
    {
        predicate = null;
        diagnostic = null;

        if (argumentExpression is not ParenthesizedLambdaExpressionSyntax
            {
                ParameterList.Parameters.Count: 2,
                ExpressionBody: { } body
            } lambda)
        {
            diagnostic = new SVCFY012(argumentExpression.GetLocation()).CreateDiagnostic();
            return false;
        }

        var nsParam = lambda.ParameterList.Parameters[0].Identifier.ValueText;
        var nameParam = lambda.ParameterList.Parameters[1].Identifier.ValueText;

        if (TryBuildEvaluator(body, nsParam, nameParam, out predicate, out diagnostic) && predicate is not null)
            return true;

        diagnostic ??= new SVCFY012(body.GetLocation()).CreateDiagnostic();
        return false;
    }

    private static bool TryBuildEvaluator(ExpressionSyntax expr, string nsParam, string nameParam, out Func<string, string, bool>? evaluator, out Diagnostic? diagnostic)
    {
        diagnostic = null;

        switch (expr)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                evaluator = (_, _) => true;
                return true;

            case ParenthesizedExpressionSyntax paren:
                return TryBuildEvaluator(paren.Expression, nsParam, nameParam, out evaluator, out diagnostic);

            case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression):
                if (!TryBuildEvaluator(unary.Operand, nsParam, nameParam, out var negated, out diagnostic) || negated is null)
                {
                    evaluator = null;
                    return false;
                }

                evaluator = (ns, name) => !negated(ns, name);
                return true;

            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.LogicalAndExpression):
                return TryBuildLogical(bin, nsParam, nameParam, isAnd: true, out evaluator, out diagnostic);

            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.LogicalOrExpression):
                return TryBuildLogical(bin, nsParam, nameParam, isAnd: false, out evaluator, out diagnostic);

            case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.EqualsExpression) || bin.IsKind(SyntaxKind.NotEqualsExpression):
                return TryBuildEquality(bin, nsParam, nameParam, out evaluator);

            case InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } mae
            } invocation when id.Identifier.ValueText == nsParam || id.Identifier.ValueText == nameParam:
                return TryBuildStringMethod(id.Identifier.ValueText == nsParam, mae.Name.Identifier.ValueText, invocation.ArgumentList.Arguments, out evaluator, out diagnostic);

            default:
                evaluator = null;
                diagnostic = new SVCFY012(expr.GetLocation()).CreateDiagnostic();
                return false;
        }
    }

    private static bool TryBuildLogical(BinaryExpressionSyntax bin, string nsParam, string nameParam, bool isAnd, out Func<string, string, bool>? evaluator, out Diagnostic? diagnostic)
    {
        if (!TryBuildEvaluator(bin.Left, nsParam, nameParam, out var left, out diagnostic) || left is null)
        {
            evaluator = null;
            return false;
        }

        if (!TryBuildEvaluator(bin.Right, nsParam, nameParam, out var right, out diagnostic) || right is null)
        {
            evaluator = null;
            return false;
        }

        evaluator = isAnd
            ? (ns, name) => left(ns, name) && right(ns, name)
            : (ns, name) => left(ns, name) || right(ns, name);
        return true;
    }

    private static bool TryBuildEquality(BinaryExpressionSyntax bin, string nsParam, string nameParam, out Func<string, string, bool>? evaluator)
    {
        evaluator = null;

        if (!TryGetParameterAndLiteral(bin.Left, bin.Right, nsParam, nameParam, out var isNs, out var literal)
            && !TryGetParameterAndLiteral(bin.Right, bin.Left, nsParam, nameParam, out isNs, out literal))
            return false;

        var expectEqual = bin.IsKind(SyntaxKind.EqualsExpression);
        evaluator = isNs
            ? (ns, name) => (ns == literal) == expectEqual
            : (ns, name) => (name == literal) == expectEqual;
        return true;
    }

    private static bool TryGetParameterAndLiteral(ExpressionSyntax left, ExpressionSyntax right, string nsParam, string nameParam, out bool isNs, out string literal)
    {
        if (IsIdentifier(left, nsParam) && TryGetStringLiteral(right, out literal)) { isNs = true; return true; }
        if (IsIdentifier(left, nameParam) && TryGetStringLiteral(right, out literal)) { isNs = false; return true; }

        isNs = false;
        literal = "";
        return false;
    }

    private static bool TryBuildStringMethod(
        bool isNs,
        string methodName,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        out Func<string, string, bool>? evaluator,
        out Diagnostic? diagnostic)
    {
        evaluator = null;
        diagnostic = null;

        if (!TryBuildStringEvaluator(methodName, arguments, out var stringEvaluator, out diagnostic) || stringEvaluator is null)
            return false;

        evaluator = isNs
            ? (ns, name) => stringEvaluator(ns)
            : (ns, name) => stringEvaluator(name);
        return true;
    }

    private static bool TryBuildStringEvaluator(
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
        "StartsWith" => s => s.StartsWith(value, comparison),
        "EndsWith" => s => s.EndsWith(value, comparison),
        "Contains" => s => s.Contains(value, comparison),
        "Equals" => s => s.Equals(value, comparison),
        _ => null
    };

    private static Func<string, bool>? BuildMulti(string methodName, List<string> values, StringComparison comparison) => methodName switch
    {
        "StartsWith" => s => values.Any(value => s.StartsWith(value, comparison)),
        "EndsWith" => s => values.Any(value => s.EndsWith(value, comparison)),
        "Contains" => s => values.Any(value => s.Contains(value, comparison)),
        _ => null
    };

    private static bool IsIdentifier(ExpressionSyntax expr, string name) =>
        expr is IdentifierNameSyntax id && id.Identifier.ValueText == name;

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
