using Microsoft.CodeAnalysis;

namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByNamespaceOf&lt;TMarker&gt;(predicate, lifetime)</c> call site, reduced to
/// the marker type's root namespace plus a pure namespace-matching function and the data
/// needed to dispatch to it at runtime.
/// </summary>
internal sealed record NamespaceOfConventionRule(
    INamedTypeSymbol MarkerType,
    string MarkerTypeFqn,
    string RootNamespace,
    string PredicateExpression,
    string Lifetime,
    Func<string, bool> Predicate);
