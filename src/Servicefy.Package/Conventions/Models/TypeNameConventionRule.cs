namespace Servicefy.Package.Conventions.Models;

/// <summary>
/// A single <c>.ByTypeName(predicate, lifetime)</c> call site, reduced to a pure
/// (namespace, type name)-matching function plus the data needed to dispatch to it at runtime.
/// </summary>
internal sealed record TypeNameConventionRule(
    string PredicateExpression,
    string Lifetime,
    Func<string, string, bool> Predicate);
