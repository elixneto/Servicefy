using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Servicefy.Package.Pipeline;

namespace Servicefy.Package.Decorators.Analysis;

/// <summary>
/// A single <c>.Decorate&lt;TService, TDecorator&gt;()</c> or
/// <c>.Decorate(typeof(IFoo&lt;&gt;), typeof(FooDecorator&lt;&gt;))</c> call site, reduced to
/// value-equatable data so the incremental pipeline can cache it: types are kept as
/// <see cref="TypeReference"/> ids and resolved against the current compilation when consumed.
/// </summary>
/// <remarks>
/// <see cref="Location"/> is kept as-is (not as <see cref="LocationInfo"/>): decorator chains are
/// ordered by source tree and span, and diagnostics must point into the compilation's own trees.
/// A source location compares equal across compilations as long as its file was not edited, which
/// is exactly when the call site is unchanged.
/// </remarks>
internal sealed record DecorateCall(string ServiceId, string DecoratorId, bool IsOpenGeneric, Location Location);

/// <summary>
/// Finds <c>.Decorate(...)</c> call sites. Syntax-only <see cref="IsCandidate"/> plus a semantic
/// <see cref="Transform"/>, wired into the generators' <c>SyntaxProvider</c> so only edited files are
/// re-examined, instead of walking every syntax tree of the compilation on each run.
/// </summary>
internal static class DecorateCallCollector
{
    internal static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation
        && (IsClosedCandidate(invocation, out _) || IsOpenGenericCandidate(invocation, out _, out _));

    internal static DecorateCall? Transform(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (IsOpenGenericCandidate(invocation, out var serviceTypeOf, out var decoratorTypeOf))
        {
            // .Decorate(typeof(IFoo<>), typeof(FooDecorator<>)) — only unbound generics; closed or
            // non-generic typeof arguments belong to the generic overload and are ignored.
            if (semanticModel.GetTypeInfo(serviceTypeOf!.Type).Type is not INamedTypeSymbol { IsUnboundGenericType: true } service
                || semanticModel.GetTypeInfo(decoratorTypeOf!.Type).Type is not INamedTypeSymbol { IsUnboundGenericType: true } decorator)
                return null;

            return Create(service.OriginalDefinition, decorator.OriginalDefinition, isOpenGeneric: true, invocation.GetLocation());
        }

        if (IsClosedCandidate(invocation, out var generic))
        {
            if (semanticModel.GetTypeInfo(generic!.TypeArgumentList.Arguments[0]).Type is not INamedTypeSymbol serviceType
                || semanticModel.GetTypeInfo(generic.TypeArgumentList.Arguments[1]).Type is not INamedTypeSymbol decoratorType)
                return null;

            return Create(serviceType, decoratorType, isOpenGeneric: false, invocation.GetLocation());
        }

        return null;
    }

    /// <summary>
    /// Resolves the <c>.Decorate(typeof(IFoo&lt;&gt;), typeof(FooDecorator&lt;&gt;))</c> calls to their
    /// unbound generic definitions, in call-site order.
    /// </summary>
    internal static List<(INamedTypeSymbol UnboundService, INamedTypeSymbol UnboundDecorator, Location Location)> ResolveOpenGeneric(
        Compilation compilation, IEnumerable<DecorateCall> calls) =>
        Resolve(compilation, calls, openGeneric: true);

    /// <summary>Resolves the <c>.Decorate&lt;TService, TDecorator&gt;()</c> calls, in call-site order.</summary>
    internal static List<(INamedTypeSymbol Service, INamedTypeSymbol Decorator, Location Location)> ResolveClosed(
        Compilation compilation, IEnumerable<DecorateCall> calls) =>
        Resolve(compilation, calls, openGeneric: false);

    private static List<(INamedTypeSymbol, INamedTypeSymbol, Location)> Resolve(
        Compilation compilation, IEnumerable<DecorateCall> calls, bool openGeneric)
    {
        var results = new List<(INamedTypeSymbol, INamedTypeSymbol, Location)>();

        foreach (var call in calls)
        {
            if (call.IsOpenGeneric != openGeneric) continue;

            var service = ResolveType(compilation, call.ServiceId);
            var decorator = ResolveType(compilation, call.DecoratorId);
            if (service is null || decorator is null) continue;

            results.Add((service, decorator, call.Location));
        }

        return results;
    }

    private static DecorateCall? Create(INamedTypeSymbol service, INamedTypeSymbol decorator, bool isOpenGeneric, Location location)
    {
        var serviceId = CreateTypeId(service);
        var decoratorId = CreateTypeId(decorator);
        return serviceId is null || decoratorId is null
            ? null
            : new DecorateCall(serviceId, decoratorId, isOpenGeneric, location);
    }

    // A type argument that does not bind (a typo while typing, or a type another generator has not
    // produced yet) is still an error type the call site refers to: the decorator must stay excluded
    // from convention registration, and an unresolved decorator must still fail validation (SVCFY013)
    // at the call. Such types can't get a documentation id, so they are kept by name and recreated as
    // error types when resolved.
    private const string ErrorTypePrefix = "!error:";

    private static string? CreateTypeId(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Error
            ? ErrorTypePrefix + type.ToDisplayString()
            : TypeReference.CreateId(type);

    private static INamedTypeSymbol? ResolveType(Compilation compilation, string id) =>
        id.StartsWith(ErrorTypePrefix, StringComparison.Ordinal)
            ? compilation.CreateErrorTypeSymbol(container: null, id.Substring(ErrorTypePrefix.Length), arity: 0)
            : TypeReference.Resolve(compilation, id);

    // .Decorate<TService, TDecorator>()
    private static bool IsClosedCandidate(InvocationExpressionSyntax invocation, out GenericNameSyntax? generic)
    {
        generic = invocation.Expression is MemberAccessExpressionSyntax
        {
            Name: GenericNameSyntax { Identifier.ValueText: "Decorate", TypeArgumentList.Arguments.Count: 2 } name
        }
            ? name
            : null;

        return generic is not null;
    }

    // .Decorate(typeof(IFoo<>), typeof(FooDecorator<>))
    private static bool IsOpenGenericCandidate(
        InvocationExpressionSyntax invocation, out TypeOfExpressionSyntax? serviceTypeOf, out TypeOfExpressionSyntax? decoratorTypeOf)
    {
        serviceTypeOf = null;
        decoratorTypeOf = null;

        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: "Decorate" } }
            || invocation.ArgumentList.Arguments.Count != 2
            || invocation.ArgumentList.Arguments[0].Expression is not TypeOfExpressionSyntax service
            || invocation.ArgumentList.Arguments[1].Expression is not TypeOfExpressionSyntax decorator)
            return false;

        serviceTypeOf = service;
        decoratorTypeOf = decorator;
        return true;
    }
}
