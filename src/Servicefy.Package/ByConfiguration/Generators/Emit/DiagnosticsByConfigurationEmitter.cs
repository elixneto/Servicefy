using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.ByConfiguration.Generators.Analysis;
using Servicefy.Package.Diagnostics;

namespace Servicefy.Package.ByConfiguration.Generators.Emit;

/// <summary>
/// Validates Servicefy attribute usage on class declarations and reports
/// compile-time diagnostics (SVCFY001–SVCFY006).
/// </summary>
internal static class DiagnosticsByConfigurationEmitter
{
    internal static void Emit(
        SourceProductionContext spc,
        (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes) source)
    {
        var (compilation, classes) = source;

        foreach (var classDecl in classes)
        {
            var model  = compilation.GetSemanticModel(classDecl.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (symbol is null) continue;

            foreach (var attr in symbol.GetAttributes())
                Validate(spc, attr, classDecl, symbol, compilation);
        }
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private static void Validate(
        SourceProductionContext spc,
        AttributeData attr,
        ClassDeclarationSyntax classDecl,
        INamedTypeSymbol symbol,
        Compilation compilation)
    {
        var attrLocation  = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation();
        var classLocation = classDecl.Identifier.GetLocation();

        if (AttributeParser.TryGetServiceRegistration(attr, out var lifetime, out var explicitType))
        {
            ValidateServiceRegistration(spc, attr, symbol, compilation, lifetime, explicitType, attrLocation);
            return;
        }

        if (AttributeParser.TryGetSelfServiceRegistration(attr, out var selfLifetime))
        {
            ValidateSelfRegistration(spc, symbol, selfLifetime, attrLocation);
            return;
        }

        if (AttributeParser.TryGetKeyedServiceRegistration(attr, out _, out _, out var keyedType))
        {
            ValidateKeyedRegistration(spc, attr, symbol, compilation, keyedType, attrLocation);
            return;
        }

        if (AttributeParser.TryGetConfigureRegistration(attr, out var section, out var configLifetime))
        {
            ValidateConfigureRegistration(spc, symbol, section, configLifetime, attrLocation, classLocation);
            return;
        }

        if (AttributeParser.TryGetDecoratorAttribute(attr))
            ValidateDecoratorAttribute(spc, symbol, attrLocation);
    }

    private static void ValidateDecoratorAttribute(
        SourceProductionContext spc,
        INamedTypeSymbol symbol,
        Location? attrLocation)
    {
        if (symbol.Interfaces.Length != 1)
            spc.ReportDiagnostic(new SVCFY014(attrLocation).CreateDiagnostic(symbol.Name, symbol.Interfaces.Length));
    }

    private static void ValidateServiceRegistration(
        SourceProductionContext spc,
        AttributeData attr,
        INamedTypeSymbol symbol,
        Compilation compilation,
        string? lifetime,
        INamedTypeSymbol? explicitType,
        Location? attrLocation)
    {
        if (lifetime is null)
        {
            spc.ReportDiagnostic(new SVCFY001(attrLocation).CreateDiagnostic(symbol.Name));
            return;
        }

        explicitType ??= AttributeParser.TryGetServiceTypeFromTypeofSyntax(attr, compilation);
        ValidateInterfaceResolution(spc, symbol, explicitType, attrLocation);
    }

    private static void ValidateSelfRegistration(
        SourceProductionContext spc,
        INamedTypeSymbol symbol,
        string? lifetime,
        Location? attrLocation)
    {
        if (lifetime is null)
        {
            spc.ReportDiagnostic(new SVCFY001(attrLocation).CreateDiagnostic(symbol.Name));
            return;
        }

        if (symbol.IsAbstract || symbol.IsStatic)
            spc.ReportDiagnostic(new SVCFY009(attrLocation).CreateDiagnostic(symbol.Name));
    }

    private static void ValidateKeyedRegistration(
        SourceProductionContext spc,
        AttributeData attr,
        INamedTypeSymbol symbol,
        Compilation compilation,
        INamedTypeSymbol? explicitType,
        Location? attrLocation)
    {
        explicitType ??= AttributeParser.TryGetKeyedServiceTypeFromArgs(attr, compilation);
        ValidateInterfaceResolution(spc, symbol, explicitType, attrLocation);
    }

    private static void ValidateConfigureRegistration(
        SourceProductionContext spc,
        INamedTypeSymbol symbol,
        string? section,
        string? lifetime,
        Location? attrLocation,
        Location classLocation)
    {
        if (section is null || lifetime is null)
        {
            spc.ReportDiagnostic(new SVCFY005(attrLocation).CreateDiagnostic(symbol.Name));
            return;
        }

        var hasDefaultCtor = !symbol.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared)
                             || symbol.InstanceConstructors.Any(c => c.Parameters.Length == 0);

        if (!hasDefaultCtor)
            spc.ReportDiagnostic(new SVCFY006(classLocation).CreateDiagnostic(symbol.Name));
    }

    /// <summary>
    /// Validates that the resolved or inferred service type is consistent with the
    /// interfaces implemented by <paramref name="symbol"/>.
    /// Reports SVCFY002 or SVCFY003 as appropriate.
    /// </summary>
    private static void ValidateInterfaceResolution(
        SourceProductionContext spc,
        INamedTypeSymbol symbol,
        INamedTypeSymbol? explicitType,
        Location? attrLocation)
    {
        if (explicitType is not null)
        {
            if (!symbol.AllInterfaces.Contains(explicitType, SymbolEqualityComparer.Default))
                spc.ReportDiagnostic(new SVCFY002(attrLocation).CreateDiagnostic(symbol.Name, explicitType.Name));
            return;
        }

        if (symbol.Interfaces.Length == 0)
            spc.ReportDiagnostic(new SVCFY003(attrLocation).CreateDiagnostic(symbol.Name));
    }
}
