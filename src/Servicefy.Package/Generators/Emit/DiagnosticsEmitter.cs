using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Diagnostics;
using Servicefy.Package.Generators.Analysis;

namespace Servicefy.Package.Generators.Emit;

/// <summary>
/// Validates Servicefy attribute usage on class declarations and reports
/// compile-time diagnostics (SVCFY001–SVCFY006).
/// </summary>
internal static class DiagnosticsEmitter
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

        if (AttributeParser.TryGetKeyedServiceRegistration(attr, out _, out _, out var keyedType))
        {
            ValidateKeyedRegistration(spc, attr, symbol, compilation, keyedType, attrLocation);
            return;
        }

        if (AttributeParser.TryGetConfigureRegistration(attr, out var section, out var configLifetime))
            ValidateConfigureRegistration(spc, symbol, section, configLifetime, attrLocation, classLocation);
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
    /// Reports SVCFY002, SVCFY003, or SVCFY004 as appropriate.
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

        var ifaces = symbol.Interfaces;
        if (ifaces.Length == 0)
            spc.ReportDiagnostic(new SVCFY003(attrLocation).CreateDiagnostic(symbol.Name));
        else if (ifaces.Length > 1)
            spc.ReportDiagnostic(new SVCFY004(attrLocation).CreateDiagnostic(symbol.Name));
    }
}
