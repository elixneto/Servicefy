using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY017(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY017";
    public override string Title => "ConventionScope.AllReferences combined with a broad predicate";
    protected override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public Diagnostic CreateDiagnostic(string predicateExpression)
        => base.Create($"'{predicateExpression}' combined with ConventionScope.AllReferences matches types in every referenced package, transitive dependencies included, and makes the generator scan all of them. Narrow the predicate to your own namespaces (e.g. ns => ns.StartsWith(\"MyCompany.\")) or use the default ConventionScope.Compilation.");
}
