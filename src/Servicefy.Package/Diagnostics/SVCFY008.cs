using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY008(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY008";
    public override string Title => "Decorator class registered with non-keyed [Add*] attribute";

    public Diagnostic CreateDiagnostic(string decoratorName, string interfaceName)
        => base.Create($"The class '{decoratorName}' is used as a decorator for '{interfaceName}' and must not be registered with [AddScoped], [AddTransient], or [AddSingleton]. Use [AddKeyed*] instead, or remove the [Add*] attribute.");
}
