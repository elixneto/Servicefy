using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Models;

namespace Servicefy.Package.Conventions.Generators.Analysis;

/// <summary>
/// Parses the optional <c>scope</c> argument of <c>ByNamespace</c> / <c>ByTypeName</c> and recognizes
/// predicates that match everything, for the <c>ConventionScope.AllReferences</c> broad-predicate warning.
/// </summary>
internal static class ConventionScopeParser
{
    /// <summary>
    /// The scope of a <c>(predicate, lifetime[, scope])</c> call: <see cref="ConventionScopes.Compilation"/>
    /// when the argument is omitted, otherwise the <c>ConventionScope.X</c> member it names. False when the
    /// third argument is something else (e.g. a variable), which the generator cannot evaluate.
    /// </summary>
    internal static bool TryGetScope(SeparatedSyntaxList<ArgumentSyntax> arguments, out string scope)
    {
        scope = ConventionScopes.Compilation;
        if (arguments.Count == 2) return true;
        if (arguments.Count != 3) return false;

        var argument = arguments[2];
        if (argument.NameColon is { Name.Identifier.ValueText: not "scope" }) return false;

        if (EnumMemberAccessParser.TryGetMember(argument.Expression, "ConventionScope", out var member)
            && member is ConventionScopes.Compilation or ConventionScopes.AllReferences)
        {
            scope = member;
            return true;
        }

        return false;
    }

    /// <summary>Whether a predicate lambda's body is the literal <c>true</c> (e.g. <c>_ =&gt; true</c>).</summary>
    internal static bool IsMatchAll(ExpressionSyntax predicate)
    {
        var body = predicate switch
        {
            SimpleLambdaExpressionSyntax simple => simple.ExpressionBody,
            ParenthesizedLambdaExpressionSyntax paren => paren.ExpressionBody,
            _ => null
        };

        while (body is ParenthesizedExpressionSyntax parenthesized)
            body = parenthesized.Expression;

        return body is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.TrueLiteralExpression);
    }
}
