using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Decorators;

/// <summary>Merged decorator chain entry for one decorated interface, built from
/// <c>[DecoratorFor&lt;T&gt;]</c>-attributed classes and/or <c>.Decorate&lt;,&gt;()</c> calls.</summary>
internal sealed class InterfaceDecoratorEntry(
    string assemblyKey,
    string @namespace,
    string interfaceFqn,
    INamedTypeSymbol interfaceSymbol,
    IReadOnlyList<string> decoratorFqns,
    IReadOnlyList<INamedTypeSymbol> decoratorSymbols,
    Location? diagnosticLocation,
    IReadOnlyList<(string DecoratorFqn, Location? Location)> duplicateDecorators)
{
    internal string AssemblyKey { get; } = assemblyKey;
    internal string Namespace { get; } = @namespace;
    internal string InterfaceFqn { get; } = interfaceFqn;
    internal INamedTypeSymbol InterfaceSymbol { get; } = interfaceSymbol;
    internal IReadOnlyList<string> DecoratorFqns { get; } = decoratorFqns;
    internal IReadOnlyList<INamedTypeSymbol> DecoratorSymbols { get; } = decoratorSymbols;
    internal Location? DiagnosticLocation { get; } = diagnosticLocation;

    /// <summary>Decorator types that were declared more than once for this interface (e.g. via
    /// both <c>[DecoratorFor&lt;T&gt;]</c>/<c>[Decorator]</c> and <c>.Decorate&lt;,&gt;()</c>) —
    /// the extra declarations are dropped from <see cref="DecoratorFqns"/> and reported as SVCFY015.</summary>
    internal IReadOnlyList<(string DecoratorFqn, Location? Location)> DuplicateDecorators { get; } = duplicateDecorators;
}
