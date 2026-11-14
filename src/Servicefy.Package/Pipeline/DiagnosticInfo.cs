using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Pipeline;

/// <summary>
/// Value-equatable snapshot of a <see cref="Diagnostic"/> produced while collecting call sites,
/// so per-call-site pipeline results compare equal across compilations when nothing changed.
/// Rebuilt into a real <see cref="Diagnostic"/> with <see cref="ToDiagnostic"/> when reported.
/// </summary>
internal sealed record DiagnosticInfo(
    string Id,
    string Title,
    string Message,
    string Category,
    DiagnosticSeverity Severity,
    LocationInfo? Location)
{
    public static DiagnosticInfo From(Diagnostic diagnostic) => new(
        diagnostic.Id,
        diagnostic.Descriptor.Title.ToString(),
        diagnostic.GetMessage(),
        diagnostic.Descriptor.Category,
        diagnostic.Severity,
        LocationInfo.From(diagnostic.Location));

    public Diagnostic ToDiagnostic()
    {
        // The message is already formatted; pass it as the format with no arguments, the same way
        // ServicefyDiagnosticBase builds its descriptors.
        var descriptor = new DiagnosticDescriptor(Id, Title, Message, Category, Severity, isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location?.ToLocation());
    }
}
