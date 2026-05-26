using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY012(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY012";
    public override string Title => "Unsupported predicate expression";

    public Diagnostic CreateDiagnostic()
        => base.Create("This predicate expression has a shape Servicefy cannot evaluate at generation time. This call site will be ignored.");
}
