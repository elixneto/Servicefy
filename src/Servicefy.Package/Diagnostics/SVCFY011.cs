using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY011(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY011";
    public override string Title => "Non-literal element in StartsWith/EndsWith/Contains array argument";

    public Diagnostic CreateDiagnostic(string methodName)
        => base.Create($"The array passed to '{methodName}' must contain only string literals so Servicefy can evaluate it at generation time. This call site will be ignored.");
}
