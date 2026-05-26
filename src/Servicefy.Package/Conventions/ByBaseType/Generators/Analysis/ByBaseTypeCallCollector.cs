using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Conventions.Models;

namespace Servicefy.Package.Conventions.ByBaseType.Generators.Analysis;

/// <summary>
/// Finds <c>.ByBaseType&lt;TBase&gt;(lifetime, selector, matchAttribute)</c> invocations anywhere in
/// the user's syntax trees and reduces each to a <see cref="ByBaseTypeConventionRule"/>.
/// </summary>
internal static class ByBaseTypeCallCollector
{
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    Identifier.ValueText: "ByBaseType",
                    TypeArgumentList.Arguments.Count: 1
                }
            },
            ArgumentList.Arguments.Count: >= 1 and <= 3
        };

    internal static ByBaseTypeConventionRule? Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var genericName = (GenericNameSyntax)memberAccess.Name;
        var arguments = invocation.ArgumentList.Arguments;
        var semanticModel = context.SemanticModel;

        var baseTypeSyntax = genericName.TypeArgumentList.Arguments[0];
        if (semanticModel.GetTypeInfo(baseTypeSyntax).Type is not INamedTypeSymbol baseType)
            return null;

        if (!TryGetArgument(arguments, "lifetime", 0, out var lifetimeArg) || lifetimeArg is null)
            return null;

        if (!EnumMemberAccessParser.TryGetMember(lifetimeArg.Expression, "Lifetime", out var lifetime)
            || lifetime is not ("Singleton" or "Scoped" or "Transient"))
            return null;

        var selector = "BaseType";
        if (TryGetArgument(arguments, "selector", 1, out var selectorArg) && selectorArg is not null)
        {
            if (!EnumMemberAccessParser.TryGetMember(selectorArg.Expression, "ServiceTypeSelector", out selector)
                || selector is not ("BaseType" or "ImplementedInterfaces" or "Self" or "SelfWithInterfaces"))
                return null;
        }

        INamedTypeSymbol? attributeType = null;
        if (TryGetArgument(arguments, "matchAttribute", 2, out var attributeArg) && attributeArg is not null)
        {
            if (attributeArg.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                attributeType = null;
            }
            else if (attributeArg.Expression is TypeOfExpressionSyntax typeOfExpression)
            {
                if (semanticModel.GetTypeInfo(typeOfExpression.Type).Type is not INamedTypeSymbol resolvedAttributeType)
                    return null;

                attributeType = resolvedAttributeType;
            }
            else
            {
                return null;
            }
        }

        return new ByBaseTypeConventionRule(
            baseType,
            baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            lifetime,
            selector,
            attributeType,
            attributeType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            baseTypeSyntax.GetLocation());
    }

    private static bool TryGetArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments, string paramName, int positionalIndex, out ArgumentSyntax? argument)
    {
        foreach (var arg in arguments)
        {
            if (arg.NameColon?.Name.Identifier.ValueText == paramName)
            {
                argument = arg;
                return true;
            }
        }

        var positionalCount = 0;
        foreach (var arg in arguments)
        {
            if (arg.NameColon is not null) break;
            positionalCount++;
        }

        if (positionalIndex < positionalCount)
        {
            argument = arguments[positionalIndex];
            return true;
        }

        argument = null;
        return false;
    }
}
