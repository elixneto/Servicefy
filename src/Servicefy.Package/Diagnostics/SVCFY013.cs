using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY013(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY013";
    public override string Title => "Decorator does not implement the decorated service type";

    public Diagnostic CreateDiagnostic(string decoratorName, string serviceName)
        => base.Create($"The decorator '{decoratorName}' does not implement '{serviceName}'.");
}
