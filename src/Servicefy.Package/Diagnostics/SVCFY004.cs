using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY004(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY004";
    public override string Title => "Ambiguous service type";
    
    public Diagnostic CreateDiagnostic(string symbolName)
        => base.Create($"The class {symbolName} implements multiple interfaces. Use a generic Add attribute to select the service type.");
}