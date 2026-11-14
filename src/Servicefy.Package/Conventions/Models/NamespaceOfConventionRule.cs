namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByNamespaceOf&lt;TMarker&gt;(predicate, lifetime)</c> call site, reduced to
/// the marker type's root namespace, the predicate's source text and the data needed to
/// dispatch to it at runtime.
/// </summary>
/// <remarks>
/// Value-equatable on purpose: no symbol for <c>TMarker</c> (only its FQN and namespace) and the
/// predicate kept as text, re-parsed into a namespace-matching function at emit time.
/// </remarks>
internal sealed record NamespaceOfConventionRule(
    string MarkerTypeFqn,
    string RootNamespace,
    string PredicateExpression,
    string Lifetime);
