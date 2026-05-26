using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByBaseType&lt;TBase&gt;(lifetime, selector, attribute)</c> call site, reduced to
/// the data needed to match candidate types and dispatch to it at runtime.
/// </summary>
internal sealed record ByBaseTypeConventionRule(
    INamedTypeSymbol BaseType,
    string BaseTypeFqn,
    string Lifetime,
    string Selector,
    INamedTypeSymbol? AttributeType,
    string? AttributeFqn,
    Location? BaseTypeLocation);
