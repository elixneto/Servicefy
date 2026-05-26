using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY009(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY009";
    public override string Title => "Self-registration attribute applied to a non-concrete class";

    public Diagnostic CreateDiagnostic(string className)
        => base.Create($"The class '{className}' is abstract or static and cannot be registered with [AddSelf*]. Self-registration requires a concrete, instantiable class.");
}
