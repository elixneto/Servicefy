using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY002(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY002";
    public override string Title => "Class does not implement service type";
    
    public Diagnostic CreateDiagnostic(string symbolName, string explicitServiceTypeName)
        => base.Create($"The class {symbolName} must implement {explicitServiceTypeName}.");
}