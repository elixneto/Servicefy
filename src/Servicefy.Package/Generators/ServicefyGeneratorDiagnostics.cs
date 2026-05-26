using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Diagnostics;

namespace Servicefy.Package.Generators;

public static class ServicefyGeneratorDiagnostics
{
    public static void Emit(
        SourceProductionContext spc,
        (Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> Classes)
            source)
    {
        var (compilation, classes) = source;

        foreach (var classDecl in classes)
        {
            var model = compilation.GetSemanticModel(classDecl.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            if (symbol == null)
            {
                continue;
            }

            foreach (var attr in symbol.GetAttributes())
            {
                ValidateServiceLifetimeAndDiagnostics(spc, attr, classDecl, symbol, compilation);
            }
        }
    }

    private static void ValidateServiceLifetimeAndDiagnostics(SourceProductionContext spc, AttributeData attr,
        ClassDeclarationSyntax classDecl, INamedTypeSymbol symbol, Compilation compilation)
    {
        var hasServiceRegistration = ServicefyGeneratorRegistrations.TryGetServiceRegistration(attr, out var serviceLifetime, out var explicitServiceType);
        var hasConfigureRegistration = ServicefyGeneratorRegistrations.TryGetConfigureRegistration(attr, out var sectionName, out var configLifetime);
        
        var attrLocation = attr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation();
        var classNameLocation = classDecl.Identifier.GetLocation();

        if (hasServiceRegistration)
        {
            if (serviceLifetime is null)
            {
                spc.ReportDiagnostic(new SVCFY001(attrLocation).CreateDiagnostic(symbol.Name));
                return;
            }

            explicitServiceType ??= ServicefyGeneratorRegistrations.TryGetServiceTypeFromTypeofSyntax(attr, compilation);

            if (explicitServiceType is not null)
            {
                if (!symbol.AllInterfaces.Contains(explicitServiceType, SymbolEqualityComparer.Default))
                {
                    spc.ReportDiagnostic(new SVCFY002(attrLocation).CreateDiagnostic(symbol.Name, explicitServiceType.Name));
                }
            }
            else
            {
                var ifaces = symbol.Interfaces;
                if (ifaces.Length == 0)
                {
                    spc.ReportDiagnostic(new SVCFY003(attrLocation).CreateDiagnostic(symbol.Name));
                }
                else if (ifaces.Length > 1)
                {
                    spc.ReportDiagnostic(new SVCFY004(attrLocation).CreateDiagnostic(symbol.Name));
                }
            }
        }

        if (hasConfigureRegistration)
        {
            if (sectionName is null || configLifetime is null)
            {
                spc.ReportDiagnostic(new SVCFY005(attrLocation).CreateDiagnostic(symbol.Name));
                return;
            }

            var hasParameterlessCtor = !symbol.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared)
                                       || symbol.InstanceConstructors.Any(c => c.Parameters.Length == 0);

            if (!hasParameterlessCtor)
            {
                spc.ReportDiagnostic(new SVCFY006(classNameLocation).CreateDiagnostic(symbol.Name));
            }
        }
    }
}