using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY001(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY001";
    public override string Title => "Lifetime not specified";
    
    public Diagnostic CreateDiagnostic(string symbolName)
        => base.Create($"The Add attribute in {symbolName} must specify a lifetime.");
}