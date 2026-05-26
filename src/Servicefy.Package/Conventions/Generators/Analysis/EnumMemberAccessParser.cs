using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Servicefy.Package.Conventions.Generators.Analysis;

/// <summary>
/// Recognizes a <c>EnumType.Member</c> member-access expression and extracts the member
/// name, used by convention call collectors to parse <c>Lifetime</c>/<c>ServiceTypeSelector</c>
/// arguments without resorting to semantic lookups.
/// </summary>
internal static class EnumMemberAccessParser
{
    internal static bool TryGetMember(ExpressionSyntax expr, string enumTypeName, out string memberName)
    {
        if (expr is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: var typeName },
                Name.Identifier.ValueText: var member
            } && typeName == enumTypeName)
        {
            memberName = member;
            return true;
        }

        memberName = "";
        return false;
    }
}
