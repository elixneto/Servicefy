using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Conventions.Generators.Analysis;
using Servicefy.Package.Conventions.Models;
using Servicefy.Package.Diagnostics;

namespace Servicefy.Package.Conventions.ByBaseType.Generators.Analysis;

/// <summary>
/// Finds <c>.ByBaseType&lt;TBase&gt;(lifetime, selector, matchAttribute)</c> and
/// <c>.ByBaseType(typeof(IFoo&lt;&gt;), lifetime, selector, matchAttribute)</c> invocations anywhere
/// in the user's syntax trees and reduces each to a <see cref="ByBaseTypeConventionRule"/>.
/// </summary>
internal static class ByBaseTypeCallCollector
{
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation
        && invocation.Expression is MemberAccessExpressionSyntax memberAccess
        && (IsGenericCandidate(memberAccess, invocation) || IsOpenGenericCandidate(memberAccess, invocation));

    // .ByBaseType<TBase>(lifetime, selector?, matchAttribute?)
    private static bool IsGenericCandidate(MemberAccessExpressionSyntax memberAccess, InvocationExpressionSyntax invocation) =>
        memberAccess.Name is GenericNameSyntax { Identifier.ValueText: "ByBaseType", TypeArgumentList.Arguments.Count: 1 }
        && invocation.ArgumentList.Arguments.Count is >= 1 and <= 3;

    // .ByBaseType(typeof(IFoo<>), lifetime, selector?, matchAttribute?)
    private static bool IsOpenGenericCandidate(MemberAccessExpressionSyntax memberAccess, InvocationExpressionSyntax invocation) =>
        memberAccess.Name is IdentifierNameSyntax { Identifier.ValueText: "ByBaseType" }
        && invocation.ArgumentList.Arguments.Count is >= 2 and <= 4
        && invocation.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax;

    internal static (ByBaseTypeConventionRule? Rule, Diagnostic? Diagnostic) Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var arguments = invocation.ArgumentList.Arguments;
        var semanticModel = context.SemanticModel;

        var usesTypeOverload = memberAccess.Name is IdentifierNameSyntax;

        INamedTypeSymbol baseType;
        string baseTypeFqn;
        Location? baseTypeLocation;
        bool isOpenGeneric;

        // Argument layout differs between the two overloads: the Type overload takes the base type
        // as its first runtime argument, shifting lifetime/selector/matchAttribute by one.
        int lifetimeIndex, selectorIndex, attributeIndex;

        if (usesTypeOverload)
        {
            var typeOfExpression = (TypeOfExpressionSyntax)arguments[0].Expression;
            baseTypeLocation = typeOfExpression.Type.GetLocation();

            if (semanticModel.GetTypeInfo(typeOfExpression.Type).Type is not INamedTypeSymbol resolved)
                return (null, null);

            if (resolved.IsUnboundGenericType)
            {
                // typeof(IFoo<>) — open generic: match candidates against the unbound definition.
                isOpenGeneric = true;
                baseType = resolved;
                baseTypeFqn = resolved.OriginalDefinition
                    .ConstructUnboundGenericType()
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else if (resolved.IsGenericType)
            {
                // typeof(IFoo<Bar>) — closed generic: equivalent to ByBaseType<IFoo<Bar>>(...).
                isOpenGeneric = false;
                baseType = resolved;
                baseTypeFqn = resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else
            {
                // typeof(IFoo) — non-generic: the Type overload is for generics; SVCFY016.
                var name = resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return (null, new SVCFY016(baseTypeLocation).CreateDiagnostic(name));
            }

            (lifetimeIndex, selectorIndex, attributeIndex) = (1, 2, 3);
        }
        else
        {
            var genericName = (GenericNameSyntax)memberAccess.Name;
            var baseTypeSyntax = genericName.TypeArgumentList.Arguments[0];
            baseTypeLocation = baseTypeSyntax.GetLocation();

            if (semanticModel.GetTypeInfo(baseTypeSyntax).Type is not INamedTypeSymbol resolved)
                return (null, null);

            isOpenGeneric = false;
            baseType = resolved;
            baseTypeFqn = resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            (lifetimeIndex, selectorIndex, attributeIndex) = (0, 1, 2);
        }

        if (!TryGetArgument(arguments, "lifetime", lifetimeIndex, out var lifetimeArg) || lifetimeArg is null)
            return (null, null);

        if (!EnumMemberAccessParser.TryGetMember(lifetimeArg.Expression, "Lifetime", out var lifetime)
            || lifetime is not ("Singleton" or "Scoped" or "Transient"))
            return (null, null);

        var selector = "BaseType";
        if (TryGetArgument(arguments, "selector", selectorIndex, out var selectorArg) && selectorArg is not null)
        {
            if (!EnumMemberAccessParser.TryGetMember(selectorArg.Expression, "ServiceTypeSelector", out selector)
                || selector is not ("BaseType" or "ImplementedInterfaces" or "AllImplementedInterfaces" or "Self" or "SelfWithInterfaces"))
                return (null, null);
        }

        INamedTypeSymbol? attributeType = null;
        if (TryGetArgument(arguments, "matchAttribute", attributeIndex, out var attributeArg) && attributeArg is not null)
        {
            if (attributeArg.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                attributeType = null;
            }
            else if (attributeArg.Expression is TypeOfExpressionSyntax attributeTypeOf)
            {
                if (semanticModel.GetTypeInfo(attributeTypeOf.Type).Type is not INamedTypeSymbol resolvedAttributeType)
                    return (null, null);

                attributeType = resolvedAttributeType;
            }
            else
            {
                return (null, null);
            }
        }

        var rule = new ByBaseTypeConventionRule(
            baseType,
            baseTypeFqn,
            lifetime,
            selector,
            attributeType,
            attributeType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            baseTypeLocation,
            isOpenGeneric);

        return (rule, null);
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
