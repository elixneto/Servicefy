using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY016(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY016";
    public override string Title => "ByBaseType(Type) requires a generic type";

    public Diagnostic CreateDiagnostic(string typeName)
        => base.Create($"'{typeName}' is not a generic type. The ByBaseType(Type, ...) overload only accepts generic types such as typeof(IRepository<>) (open) or typeof(IRepository<Order>) (closed); for a non-generic type use ByBaseType<{typeName}>(...) instead. This call site was ignored.");
}
