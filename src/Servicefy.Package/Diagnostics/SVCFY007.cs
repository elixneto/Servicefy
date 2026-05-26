using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY007(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY007";
    public override string Title => "Decorator has no constructor parameter accepting the service type";

    public Diagnostic CreateDiagnostic(string decoratorName, string serviceName)
        => base.Create($"The decorator '{decoratorName}' has no public constructor with a parameter of type '{serviceName}'.");
}
