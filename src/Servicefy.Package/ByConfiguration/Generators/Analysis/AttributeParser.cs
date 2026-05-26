using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Servicefy.Package.ByConfiguration.Generators.Analysis;

/// <summary>
/// Parses Servicefy attribute data from Roslyn symbols into strongly-typed lifetime/key/type values.
/// All members are pure functions — no side effects, no output beyond return values.
/// </summary>
internal static class AttributeParser
{
    internal const string SingletonLifetime = "Singleton";
    internal const string ScopedLifetime    = "Scoped";
    internal const string TransientLifetime = "Transient";

    // -------------------------------------------------------------------------
    // Attribute recognition
    // -------------------------------------------------------------------------

    internal static bool TryGetServiceRegistration(
        AttributeData attribute,
        out string? lifetime,
        out INamedTypeSymbol? explicitServiceType)
    {
        lifetime = null;
        explicitServiceType = null;

        switch (attribute.AttributeClass?.Name)
        {
            case "AddAttribute":
            case "Add":
                lifetime = MapLifetime(attribute.ConstructorArguments.ElementAtOrDefault(0).Value)
                           ?? GetLifetimeFromSyntax(attribute, 0);
                break;
            case "AddScopedAttribute":
            case "AddScoped":
                lifetime = ScopedLifetime;
                break;
            case "AddSingletonAttribute":
            case "AddSingleton":
                lifetime = SingletonLifetime;
                break;
            case "AddTransientAttribute":
            case "AddTransient":
                lifetime = TransientLifetime;
                break;
            default:
                return false;
        }

        if (attribute.AttributeClass?.TypeArguments.Length > 0)
            explicitServiceType = attribute.AttributeClass.TypeArguments[0] as INamedTypeSymbol;

        return true;
    }

    internal static bool TryGetSelfServiceRegistration(
        AttributeData attribute,
        out string? lifetime)
    {
        lifetime = null;

        switch (attribute.AttributeClass?.Name)
        {
            case "AddSelfAttribute":
            case "AddSelf":
                lifetime = MapLifetime(attribute.ConstructorArguments.ElementAtOrDefault(0).Value)
                           ?? GetLifetimeFromSyntax(attribute, 0);
                break;
            case "AddSelfScopedAttribute":
            case "AddSelfScoped":
                lifetime = ScopedLifetime;
                break;
            case "AddSelfSingletonAttribute":
            case "AddSelfSingleton":
                lifetime = SingletonLifetime;
                break;
            case "AddSelfTransientAttribute":
            case "AddSelfTransient":
                lifetime = TransientLifetime;
                break;
            default:
                return false;
        }

        return true;
    }

    internal static bool TryGetKeyedServiceRegistration(
        AttributeData attribute,
        out string? lifetime,
        out string? serviceKey,
        out INamedTypeSymbol? explicitServiceType)
    {
        lifetime = null;
        serviceKey = null;
        explicitServiceType = null;

        switch (attribute.AttributeClass?.Name)
        {
            case "AddKeyedScopedAttribute":
            case "AddKeyedScoped":
                lifetime = ScopedLifetime;
                break;
            case "AddKeyedSingletonAttribute":
            case "AddKeyedSingleton":
                lifetime = SingletonLifetime;
                break;
            case "AddKeyedTransientAttribute":
            case "AddKeyedTransient":
                lifetime = TransientLifetime;
                break;
            default:
                return false;
        }

        serviceKey = FormatKeyConstant(attribute.ConstructorArguments.ElementAtOrDefault(0))
                     ?? GetKeyFromSyntax(attribute, 0);

        if (attribute.AttributeClass?.TypeArguments.Length > 0)
            explicitServiceType = attribute.AttributeClass.TypeArguments[0] as INamedTypeSymbol;

        return true;
    }

    internal static bool TryGetDecoratorForAttribute(
        AttributeData attribute,
        out INamedTypeSymbol? serviceType)
    {
        serviceType = null;
        if (attribute.AttributeClass?.Name is not ("DecoratorForAttribute" or "DecoratorFor"))
            return false;
        if (attribute.AttributeClass.TypeArguments.Length > 0)
            serviceType = attribute.AttributeClass.TypeArguments[0] as INamedTypeSymbol;
        return true;
    }

    /// <summary>
    /// Recognizes the non-generic <c>[Decorator]</c> attribute, which marks a class as a decorator
    /// for the single interface it implements (<c>TService</c> is inferred rather than specified).
    /// </summary>
    internal static bool TryGetDecoratorAttribute(AttributeData attribute)
        => attribute.AttributeClass?.Name is "DecoratorAttribute" or "Decorator";

    internal static bool TryGetConfigureRegistration(
        AttributeData attribute,
        out string? sectionName,
        out string? lifetime)
    {
        sectionName = null;
        lifetime = null;

        if (attribute.AttributeClass?.Name is not ("ConfigureAttribute" or "Configure"))
            return false;

        sectionName = attribute.ConstructorArguments.ElementAtOrDefault(0).Value as string
                      ?? GetStringFromSyntax(attribute, 0);
        lifetime    = MapLifetime(attribute.ConstructorArguments.ElementAtOrDefault(1).Value)
                      ?? GetLifetimeFromSyntax(attribute, 1);
        return true;
    }

    // -------------------------------------------------------------------------
    // Service-type resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the explicit service type from <c>typeof(T)</c> in any constructor argument.
    /// Used for standard (non-keyed) attributes where the type arg is at position 0.
    /// </summary>
    internal static INamedTypeSymbol? TryGetServiceTypeFromTypeofSyntax(
        AttributeData attribute, Compilation compilation)
    {
        foreach (var arg in attribute.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Type && arg.Value is INamedTypeSymbol resolved)
                return resolved;
        }

        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
            return null;

        var arguments = syntax.ArgumentList?.Arguments;
        if (arguments is null) return null;

        foreach (var arg in arguments)
        {
            if (arg.Expression is TypeOfExpressionSyntax typeOfExpr)
            {
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                return model.GetTypeInfo(typeOfExpr.Type).Type as INamedTypeSymbol;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the explicit service type for keyed attributes where arg[0] is the key
    /// and the optional arg[1] carries <c>typeof(T)</c>.
    /// </summary>
    internal static INamedTypeSymbol? TryGetKeyedServiceTypeFromArgs(
        AttributeData attribute, Compilation compilation)
    {
        if (attribute.ConstructorArguments.Length > 1)
        {
            var arg = attribute.ConstructorArguments[1];
            if (arg.Kind == TypedConstantKind.Type && arg.Value is INamedTypeSymbol resolved)
                return resolved;
        }

        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
            return null;

        var arguments = syntax.ArgumentList?.Arguments;
        if (arguments is null || arguments.Value.Count < 2) return null;

        if (arguments.Value[1].Expression is TypeOfExpressionSyntax typeOfExpr)
        {
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            return model.GetTypeInfo(typeOfExpr.Type).Type as INamedTypeSymbol;
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    internal static string? FormatKeyConstant(TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Error) return null;
        if (constant.IsNull) return "null";
        return constant.Value switch
        {
            string s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
            char   c => $"'{c}'",
            bool   b => b ? "true" : "false",
            { }    v => v.ToString(),
            _        => null
        };
    }

    private static string? GetKeyFromSyntax(AttributeData attribute, int argIndex)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
            return null;
        var expr = syntax.ArgumentList?.Arguments.ElementAtOrDefault(argIndex)?.Expression;
        return expr is LiteralExpressionSyntax literal ? literal.Token.Text : null;
    }

    private static string? GetLifetimeFromSyntax(AttributeData attribute, int argIndex)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
            return null;
        var expr = syntax.ArgumentList?.Arguments.ElementAtOrDefault(argIndex)?.Expression;
        if (expr is not MemberAccessExpressionSyntax mae) return null;

        return mae.Name.Identifier.ValueText switch
        {
            "Singleton" => SingletonLifetime,
            "Scoped"    => ScopedLifetime,
            "Transient" => TransientLifetime,
            _           => null
        };
    }

    private static string? GetStringFromSyntax(AttributeData attribute, int argIndex)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax)
            return null;
        var expr = syntax.ArgumentList?.Arguments.ElementAtOrDefault(argIndex)?.Expression;
        return expr is LiteralExpressionSyntax literal ? literal.Token.ValueText : null;
    }

    private static string? MapLifetime(object? value) => value switch
    {
        0 => SingletonLifetime,
        1 => ScopedLifetime,
        2 => TransientLifetime,
        _ => null
    };
}
