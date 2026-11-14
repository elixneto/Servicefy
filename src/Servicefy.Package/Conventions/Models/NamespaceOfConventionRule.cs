namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByNamespaceOf&lt;TMarker&gt;(predicate, lifetime)</c> call site, reduced to
/// the marker type's root namespace, the predicate's source text and the data needed to
/// dispatch to it at runtime.
/// </summary>
/// <remarks>
/// Value-equatable on purpose: no symbol for <c>TMarker</c> (only its FQN, its
/// <see cref="Pipeline.TypeReference"/> id and its namespace) and the predicate kept as text,
/// re-parsed into a namespace-matching function at emit time. The id is resolved at emit time to
/// find the assembly declaring <c>TMarker</c> (a NuGet package anchors that package, issue #8).
/// </remarks>
internal sealed record NamespaceOfConventionRule(
    string MarkerTypeFqn,
    string? MarkerTypeId,
    string RootNamespace,
    string PredicateExpression,
    string Lifetime);
