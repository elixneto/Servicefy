using Servicefy.Package.Pipeline;

namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByBaseType&lt;TBase&gt;(lifetime, selector, attribute)</c> or
/// <c>.ByBaseType(typeof(IFoo&lt;&gt;), lifetime, selector, attribute)</c> call site, reduced to
/// the data needed to match candidate types and dispatch to it at runtime.
/// </summary>
/// <remarks>
/// Value-equatable on purpose (no symbols, syntax or locations): the incremental pipeline can
/// then tell an unchanged call site apart from a changed one and skip downstream work. Types are
/// kept as <see cref="TypeReference"/> ids and resolved against the compilation at emit time.
/// </remarks>
/// <param name="BaseTypeId">
/// <see cref="TypeReference"/> id of <c>TBase</c> (for the open-generic overload, of its definition).
/// </param>
/// <param name="BaseTypeIsInterface">Whether <c>TBase</c> is an interface (checked for SVCFY010).</param>
/// <param name="AttributeTypeId"><see cref="TypeReference"/> id of <c>matchAttribute</c>, if any.</param>
/// <param name="IsOpenGeneric">
/// <c>true</c> when the rule came from the <c>typeof(IFoo&lt;&gt;)</c> overload: matching is then
/// performed against the unbound generic definition (via <c>OriginalDefinition</c>) and
/// <c>BaseTypeFqn</c> holds the unbound form (<c>global::Ns.IFoo&lt;&gt;</c>).
/// </param>
internal sealed record ByBaseTypeConventionRule(
    string BaseTypeId,
    string BaseTypeFqn,
    bool BaseTypeIsInterface,
    string Lifetime,
    string Selector,
    string? AttributeTypeId,
    string? AttributeFqn,
    LocationInfo? BaseTypeLocation,
    bool IsOpenGeneric);
