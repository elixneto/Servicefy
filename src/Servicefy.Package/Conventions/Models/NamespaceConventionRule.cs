namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByNamespace(predicate, lifetime[, scope])</c> call site, reduced to the predicate's
/// source text plus the data needed to dispatch to it at runtime. <see cref="Scope"/> is the
/// <c>ConventionScope</c> member name (<see cref="ConventionScopes"/>).
/// </summary>
/// <remarks>
/// Value-equatable on purpose: the predicate is kept as text (not as a delegate, which only
/// compares by reference) and re-parsed into a namespace-matching function at emit time.
/// </remarks>
internal sealed record NamespaceConventionRule(
    string PredicateExpression,
    string Lifetime,
    string Scope = ConventionScopes.Compilation);
