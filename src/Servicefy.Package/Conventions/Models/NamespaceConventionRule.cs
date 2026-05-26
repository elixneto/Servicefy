namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByNamespace(predicate, lifetime)</c> call site, reduced to a pure
/// namespace-matching function plus the data needed to dispatch to it at runtime.
/// </summary>
internal sealed record NamespaceConventionRule(
    string PredicateExpression,
    string Lifetime,
    Func<string, bool> Predicate);
