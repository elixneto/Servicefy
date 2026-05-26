using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY005(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY005";
    public override string Title => "Invalid Configure arguments";

    public Diagnostic CreateDiagnostic(string symbolName)
        => base.Create($"The Configure attribute on {symbolName} must define sectionName and lifetime.");
}