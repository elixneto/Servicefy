using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY006(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY006";
    public override string Title => "Parameterless constructor required";

    public Diagnostic CreateDiagnostic(string symbolName)
        => base.Create($"The class {symbolName} needs a parameterless constructor to use Configure.");
}