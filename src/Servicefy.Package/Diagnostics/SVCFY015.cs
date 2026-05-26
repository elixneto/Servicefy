using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public class SVCFY015(Location? location) : ServicefyDiagnosticBase(location)
{
    public override string Id => "SVCFY015";
    public override string Title => "Decorator declared both via attribute and .Decorate<,>()";
    protected override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public Diagnostic CreateDiagnostic(string decoratorName, string serviceName)
        => base.Create($"The decorator '{decoratorName}' is declared both via [DecoratorFor<T>]/[Decorator] and '.Decorate<{serviceName}, {decoratorName}>()' for '{serviceName}'. The duplicate entry was ignored; declare it only one way.");
}
