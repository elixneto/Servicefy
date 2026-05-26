using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY003(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY003";
    public override string Title => "No interface found";
    
    public Diagnostic CreateDiagnostic(string symbolName)
        => base.Create($"The class {symbolName} must implement at least one interface when using Add-style attributes without a generic service type.");
}