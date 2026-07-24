using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByBaseType&lt;TBase&gt;(lifetime, selector, attribute)</c> or
/// <c>.ByBaseType(typeof(IFoo&lt;&gt;), lifetime, selector, attribute)</c> call site, reduced to
/// the data needed to match candidate types and dispatch to it at runtime.
/// </summary>
/// <param name="IsOpenGeneric">
/// <c>true</c> when the rule came from the <c>typeof(IFoo&lt;&gt;)</c> overload: matching is then
/// performed against the unbound generic definition (via <c>OriginalDefinition</c>) and
/// <c>BaseTypeFqn</c> holds the unbound form (<c>global::Ns.IFoo&lt;&gt;</c>).
/// </param>
internal sealed record ByBaseTypeConventionRule(
    INamedTypeSymbol BaseType,
    string BaseTypeFqn,
    string Lifetime,
    string Selector,
    INamedTypeSymbol? AttributeType,
    string? AttributeFqn,
    Location? BaseTypeLocation,
    bool IsOpenGeneric);
