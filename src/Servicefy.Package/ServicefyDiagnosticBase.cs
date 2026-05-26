using Microsoft.CodeAnalysis;

namespace Servicefy.Package;

public abstract class ServicefyDiagnosticBase(Location? location)
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    protected virtual DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    protected Diagnostic Create(string message)
    {
        message = message.Replace("global::", "");

        var descriptor = new DiagnosticDescriptor(Id, Title, message, category: "ServiceRegistration", Severity, true);
        return Diagnostic.Create(descriptor, location);
    }
}