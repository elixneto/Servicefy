using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Servicefy.Package;
using Servicefy.Package.Conventions.ByBaseType.Generators.Analysis;
using Servicefy.Package.Conventions.ByNamespace.Generators.Analysis;
using Servicefy.Package.Conventions.ByNamespaceOf.Generators.Analysis;
using Servicefy.Package.Conventions.ByTypeName.Generators.Analysis;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Package.Conventions.Models;
using Servicefy.Package.Pipeline;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Incremental;

public class ConventionPipelineModelTests
{
    private static readonly string[] CallSiteSteps =
    [
        TrackingNames.ByNamespaceResults, TrackingNames.ByNamespaceRules,
        TrackingNames.ByBaseTypeResults, TrackingNames.ByBaseTypeRules,
        TrackingNames.ByNamespaceOfResults, TrackingNames.ByNamespaceOfRules,
        TrackingNames.ByTypeNameResults, TrackingNames.ByTypeNameRules,
    ];

    private static readonly string[] EmitInputSteps =
    [
        TrackingNames.ByNamespaceEmitInput,
        TrackingNames.ByBaseTypeEmitInput,
        TrackingNames.ByNamespaceOfEmitInput,
        TrackingNames.ByTypeNameEmitInput,
    ];

    private static readonly Type[] ForbiddenTypes =
    [
        typeof(ISymbol),
        typeof(Compilation),
        typeof(SemanticModel),
        typeof(SyntaxNode),
        typeof(SyntaxTree),
        typeof(SyntaxToken),
        typeof(SyntaxTrivia),
        typeof(Location),
        typeof(Diagnostic),
        typeof(Delegate),
    ];

    public static TheoryData<string> ModelTypeNames => new(
        typeof(NamespaceConventionRule).FullName!,
        typeof(NamespaceOfConventionRule).FullName!,
        typeof(TypeNameConventionRule).FullName!,
        typeof(ByBaseTypeConventionRule).FullName!,
        typeof(DiagnosticInfo).FullName!,
        typeof(LocationInfo).FullName!);

    [Theory]
    [MemberData(nameof(ModelTypeNames))]
    public void RuleModel_DeclaresNoSymbolCompilationSyntaxOrDelegate(string typeName)
    {
        var type = typeof(NamespaceConventionRule).Assembly.GetType(typeName, throwOnError: true)!;

        AssertDeclaresNoForbiddenType(type);
    }

    [Theory]
    [MemberData(nameof(ModelTypeNames))]
    public void RuleModel_IsValueEquatable(string typeName)
    {
        var type = typeof(NamespaceConventionRule).Assembly.GetType(typeName, throwOnError: true)!;

        Assert.True(
            typeof(IEquatable<>).MakeGenericType(type).IsAssignableFrom(type),
            $"{type.Name} must implement IEquatable<{type.Name}>.");
        Assert.Equal(type, type.GetMethod(nameof(GetHashCode), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)?.DeclaringType);

        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        object Build() => constructor.Invoke(constructor.GetParameters().Select(p => SampleValue(p.ParameterType)).ToArray());

        var a = Build();
        var b = Build();
        Assert.NotSame(a, b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    private static object? SampleValue(Type type) =>
        type == typeof(string) ? new string("sample".ToCharArray())
        : type == typeof(bool) ? true
        : type.IsValueType ? Activator.CreateInstance(type)
        : null;

    public static TheoryData<string> CollectorNames => new(
        typeof(ByNamespaceCallCollector).FullName!,
        typeof(ByNamespaceOfCallCollector).FullName!,
        typeof(ByTypeNameCallCollector).FullName!,
        typeof(ByBaseTypeCallCollector).FullName!);

    [Theory]
    [MemberData(nameof(CollectorNames))]
    public void CallSiteTransformResult_DeclaresNoSymbolCompilationSyntaxOrDelegate(string collectorName)
    {
        var collector = typeof(NamespaceConventionRule).Assembly.GetType(collectorName, throwOnError: true)!;
        var transform = collector.GetMethod("Transform", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(transform);

        AssertDeclaresNoForbiddenType(transform.ReturnType);
    }

    [Fact]
    public void TrackedCallSiteValues_ContainNoSymbolCompilationSyntaxOrDelegate()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);

        foreach (var name in CallSiteSteps)
        {
            var values = run.InitialValues<ServicefyConventionsGenerator>(name);
            Assert.NotEmpty(values);

            foreach (var value in values)
                AssertGraphHasNoForbiddenObject(value, name);
        }
    }

    [Fact]
    public void CollectedRules_ContainNoSymbolCompilationSyntaxOrDelegate()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);

        foreach (var name in EmitInputSteps)
        {
            var collected = CollectedRules(run.InitialValues<ServicefyConventionsGenerator>(name).Single());
            AssertGraphHasNoForbiddenObject(collected, name);
        }
    }

    [Fact]
    public void TestProject_ProducesEveryKindOfRule()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);

        Assert.Empty(run.Initial.Diagnostics);

        var byBaseType = run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules)
            .Cast<ByBaseTypeConventionRule>()
            .ToList();
        Assert.Equal(RulesTestProject.ByBaseTypeCallCount, byBaseType.Count);
        Assert.Contains(byBaseType, r => r.IsOpenGeneric);
        Assert.Contains(byBaseType, r => r.AttributeFqn is not null);
        Assert.Contains(byBaseType, r => r.BaseTypeFqn == "global::TestAssembly.Domain.IRepository<global::TestAssembly.Domain.Order>");
        Assert.Contains(byBaseType, r => r.BaseTypeFqn == "global::TestAssembly.Domain.Outer.INestedHandler");
        Assert.Contains(byBaseType, r => r.BaseTypeFqn == "global::TestAssembly.Domain.Container<global::TestAssembly.Domain.Order>.IInner");

        Assert.Single(run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceRules));
        Assert.Single(run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceOfRules));
        Assert.Single(run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameRules));
    }

    [Fact]
    public void CallSiteValues_FromIndependentCompilations_AreEqual()
    {
        var first = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);
        var second = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);

        foreach (var name in CallSiteSteps)
        {
            var a = first.InitialValues<ServicefyConventionsGenerator>(name);
            var b = second.InitialValues<ServicefyConventionsGenerator>(name);

            Assert.NotEmpty(a);
            Assert.Equal(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.True(a[i].Equals(b[i]), $"'{name}'[{i}]: {a[i]} != {b[i]}");
                Assert.True(EqualityComparer<object>.Default.Equals(a[i], b[i]), $"'{name}'[{i}] (default comparer)");
                Assert.Equal(a[i].GetHashCode(), b[i].GetHashCode());
            }
        }
    }

    [Fact]
    public void RuleValues_FromIndependentCompilations_AreDistinctInstances()
    {
        var first = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);
        var second = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);

        foreach (var name in new[] { TrackingNames.ByNamespaceRules, TrackingNames.ByBaseTypeRules, TrackingNames.ByNamespaceOfRules, TrackingNames.ByTypeNameRules })
        {
            var a = first.InitialValues<ServicefyConventionsGenerator>(name);
            var b = second.InitialValues<ServicefyConventionsGenerator>(name);
            for (var i = 0; i < a.Count; i++)
                Assert.NotSame(a[i], b[i]);
        }
    }

    [Fact]
    public void CollectedRules_FromIndependentCompilations_AreEqual()
    {
        var first = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);
        var second = IncrementalGeneratorHelper.RunWithEdit(RulesTestProject.Sources, CompilationEdits.None);

        foreach (var name in EmitInputSteps)
        {
            var a = CollectedRules(first.InitialValues<ServicefyConventionsGenerator>(name).Single());
            var b = CollectedRules(second.InitialValues<ServicefyConventionsGenerator>(name).Single());

            Assert.Equal(((IEnumerable)a).Cast<object>().ToList(), ((IEnumerable)b).Cast<object>().ToList());

            Assert.True(a.Equals(b), $"'{name}': collected rules of type {a.GetType().Name} do not compare by value.");
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
    }

    [Fact]
    public void EditInDifferentCallSite_OnlyThatRuleValueChanges()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            RulesTestProject.Sources,
            CompilationEdits.ReplaceText(
                RulesTestProject.StartupPath,
                "name.EndsWith(\"Repository\") && ns.Contains(\"Domain\")",
                "name.EndsWith(\"Repository\") && ns.Contains(\"Domainz\")"));

        var before = run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameRules).Cast<TypeNameConventionRule>().Single();
        var after = run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameRules).Cast<TypeNameConventionRule>().Single();

        Assert.NotEqual(before, after);
        Assert.Equal("(ns, name) => name.EndsWith(\"Repository\") && ns.Contains(\"Domainz\")", after.PredicateExpression);
        Assert.Equal(before.Lifetime, after.Lifetime);
    }

    private sealed record WithDelegate(string Name, Func<string, bool> Predicate);

    private sealed record WithLocation(string Name, Location? Location);

    private sealed record WithNestedSymbol(string Name, (string Fqn, ISymbol? Symbol)? Pair);

    [Fact]
    public void DeclaredShapeCheck_RejectsDelegatesLocationsAndNestedSymbols()
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertDeclaresNoForbiddenType(typeof(WithDelegate)));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertDeclaresNoForbiddenType(typeof(WithLocation)));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertDeclaresNoForbiddenType(typeof(WithNestedSymbol)));
    }

    [Fact]
    public void RuntimeGraphCheck_RejectsDelegatesAndSyntaxNodes()
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertGraphHasNoForbiddenObject(new WithDelegate("x", _ => true), "sample"));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertGraphHasNoForbiddenObject(
                ("x", new[] { (object)Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName("x") }),
                "sample"));
    }

    private static object CollectedRules(object emitInputValue)
    {
        var item2 = emitInputValue.GetType().GetField("Item2");
        Assert.NotNull(item2);
        var collected = item2.GetValue(emitInputValue);
        Assert.NotNull(collected);
        return collected;
    }

    private static void AssertDeclaresNoForbiddenType(Type root)
    {
        var visited = new HashSet<Type>();
        var stack = new Stack<(Type Type, string Path)>();
        stack.Push((root, root.Name));

        while (stack.Count > 0)
        {
            var (type, path) = stack.Pop();
            if (!visited.Add(type)) continue;

            var forbidden = ForbiddenTypes.FirstOrDefault(f => f.IsAssignableFrom(type));
            Assert.True(forbidden is null, $"{path} is a {forbidden?.Name} ({type.FullName}).");
            Assert.False(type == typeof(object), $"{path} is typed as object, which can hold anything.");

            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
                continue;

            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                stack.Push((underlying, path));
                continue;
            }

            if (type.IsArray)
            {
                stack.Push((type.GetElementType()!, path + "[]"));
                continue;
            }

            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments())
                    stack.Push((argument, $"{path}<{argument.Name}>"));

            if (type.IsValueType || !(type.Namespace ?? "").StartsWith("System", StringComparison.Ordinal))
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    stack.Push((field.FieldType, $"{path}.{field.Name}"));
        }
    }

    private static void AssertGraphHasNoForbiddenObject(object? root, string what)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<(object? Value, string Path)>();
        stack.Push((root, what));

        while (stack.Count > 0)
        {
            var (value, path) = stack.Pop();
            if (value is null) continue;

            var type = value.GetType();
            var forbidden = ForbiddenTypes.FirstOrDefault(f => f.IsInstanceOfType(value));
            Assert.True(forbidden is null, $"{path} holds a {forbidden?.Name} ({type.FullName}).");

            if (type.IsPrimitive || type.IsEnum || value is string or decimal) continue;
            if (!type.IsValueType && !visited.Add(value)) continue;

            if (value is IEnumerable enumerable)
            {
                var index = 0;
                foreach (var item in enumerable)
                    stack.Push((item, $"{path}[{index++}]"));
                if (type.Assembly != typeof(NamespaceConventionRule).Assembly) continue;
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                stack.Push((field.GetValue(value), $"{path}.{field.Name}"));
        }
    }
}

internal static class RulesTestProject
{
    public const string DomainPath = "Domain.cs";
    public const string StartupPath = "Startup.cs";

    public const int ByBaseTypeCallCount = 6;

    public const string Domain = """
        namespace TestAssembly.Domain
        {
            public class Order { }

            public interface IRepository<T> { }
            public class OrderRepository : IRepository<Order> { }
            public class Repository<T> : IRepository<T> { }

            public interface IMapper<TSource, TTarget> { }
            public class OrderMapper : IMapper<Order, string> { }

            public static class Outer
            {
                public interface INestedHandler { }

                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class AutoRegisterAttribute : System.Attribute { }
            }

            public class NestedHandler : Outer.INestedHandler { }

            public class Container<T>
            {
                public interface IInner { }
            }

            public class OrderInner : Container<Order>.IInner { }

            public interface IMarkedService { }

            [Outer.AutoRegister]
            public class MarkedService : IMarkedService { }

            public class UnmarkedService : IMarkedService { }
        }
        """;

    public const string Startup = """
        using TestAssembly.Domain;

        namespace TestAssembly
        {
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddServicefyConventions().ByBaseType<IRepository<Order>>(Lifetime.Scoped);
                    services.AddServicefyConventions().ByBaseType<Outer.INestedHandler>(Lifetime.Transient);
                    services.AddServicefyConventions().ByBaseType<Container<Order>.IInner>(Lifetime.Transient);
                    services.AddServicefyConventions().ByBaseType<IMapper<Order, string>>(Lifetime.Singleton);
                    services.AddServicefyConventions().ByBaseType(typeof(IRepository<>), Lifetime.Singleton, ServiceTypeSelector.BaseType);
                    services.AddServicefyConventions().ByBaseType<IMarkedService>(Lifetime.Scoped, ServiceTypeSelector.BaseType, typeof(Outer.AutoRegisterAttribute));
                    services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("TestAssembly.Domain", StringComparison.Ordinal), Lifetime.Scoped);
                    services.AddServicefyConventions().ByNamespaceOf<Outer.INestedHandler>(ns => !ns.EndsWith(["Infrastructure", "Tests"]), Lifetime.Singleton);
                    services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith("Repository") && ns.Contains("Domain"), Lifetime.Transient);
                }
            }
        }
        """;

    public static IEnumerable<(string Path, string Source)> Sources =>
    [
        (DomainPath, Domain),
        (StartupPath, Startup),
    ];
}
