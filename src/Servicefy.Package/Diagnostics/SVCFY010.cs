using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY010(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY010";
    public override string Title => "ByBaseType Self selector requires a class base type";

    public Diagnostic CreateDiagnostic(string baseTypeName)
        => base.Create($"'{baseTypeName}' is an interface and cannot be used with ByBaseType<TBase>(..., ServiceTypeSelector.Self): Self registers the matched concrete types only and ignores TBase entirely. Use ServiceTypeSelector.BaseType, ImplementedInterfaces, or SelfWithInterfaces instead, or change TBase to a class.");
}
