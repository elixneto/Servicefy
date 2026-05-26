using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY014(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY014";
    public override string Title => "[Decorator] requires the class to implement exactly one interface";

    public Diagnostic CreateDiagnostic(string decoratorName, int interfaceCount)
        => base.Create($"The decorator '{decoratorName}' implements {interfaceCount} interface(s); [Decorator] requires exactly one so the decorated service can be inferred. Use [DecoratorFor<TService>] to specify the target interface explicitly.");
}
