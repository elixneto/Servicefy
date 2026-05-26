using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Diagnostics;

public abstract class ServicefyDiagnosticBase(Location? location)
{
    public abstract string Id { get; }
    public abstract string Title { get; }

    protected Diagnostic Create(string message)
    {
        var descriptor = new DiagnosticDescriptor(Id, Title, message, category: "ServiceRegistration", DiagnosticSeverity.Error, true);
        return Diagnostic.Create(descriptor, location);
    }
}