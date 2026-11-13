using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Servicefy.Package;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Package.Conventions.Generators.Emit;
using Servicefy.Package.Conventions.Models;
using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Helpers.ConventionScopeTestKit;

namespace Servicefy.Tests.Incremental;

public class ConventionScopeIncrementalTests
{
    private const string ConsumerPath = "Consumer.cs";

    private const string Types = """
        namespace MyApp.Utilities
        {
            public static class MathUtil
            {
                public static int Add(int a, int b) { return a + b; }
            }
        }

        namespace MyCompany.Shared.Local
        {
            public interface ILocalClock { }
            public class LocalClock : MyCompany.Shared.Clock.IClock, ILocalClock { public string Name => "local"; }
        }
        """;

    private static Func<Compilation, Compilation> SetRules(string rules) =>
        CompilationEdits.ReplaceSource(ConsumerPath, Consumer(Types, rules));

    private static IReadOnlyList<(Compilation Compilation, GeneratorDriverRunResult Result)> RunSequence(
        string rules, IEnumerable<Func<Compilation, Compilation>> edits, ReferencedAssembly[]? packages = null) =>
        IncrementalGeneratorHelper.RunSequence(
            PackageCompilationHelper.CreateCompilation(Consumer(Types, rules), packages ?? Packages()),
            edits);

    private static ConventionTypePool PoolOf(GeneratorDriverRunResult run) =>
        Assert.IsType<ConventionTypePool>(Assert.Single(
            IncrementalRun.Values(
                run.Results.Single(r => r.Generator.GetGeneratorType() == typeof(ServicefyConventionsGenerator))
                    .TrackedSteps[TrackingNames.ConventionsTypePool])));

    private static string? Generated(GeneratorDriverRunResult run, string hintName) =>
        run.Results.Single(r => r.Generator.GetGeneratorType() == typeof(ServicefyConventionsGenerator))
            .GeneratedSources.Where(s => s.HintName == hintName).Select(s => s.SourceText.ToString()).FirstOrDefault();

    private static IAssemblySymbol Package(GeneratorDriverRunResult run, string name) =>
        TypeCollectorProbe.ReferencedAssembly(PoolOf(run).Compilation, name);

    private static void AssertEveryRunEqualsAFreshRun(IReadOnlyList<(Compilation Compilation, GeneratorDriverRunResult Result)> runs)
    {
        for (var i = 0; i < runs.Count; i++)
            GeneratorOutputAssert.EqualToFreshRun(runs[i].Result, runs[i].Compilation, $"run {i}");
    }

    private static readonly Func<Compilation, Compilation>[] SourceEdits =
    [
        CompilationEdits.ReplaceText(ConsumerPath, "return a + b;", "return b + a;"),
        CompilationEdits.AddSource("Dto.cs", "namespace MyApp.Dtos { public class OrderDto { } }"),
        CompilationEdits.AddSource("ExtraClock.cs", """
            namespace MyCompany.Shared.Local
            {
                public class ExtraClock : MyCompany.Shared.Clock.IClock { public string Name => "extra"; }
            }
            """),
        CompilationEdits.ReplaceText(ConsumerPath, "return b + a;", "return a + b; // edited again"),
    ];

    [Fact]
    public void SourceEdits_PackagesAreNotWalkedAgain_AndOutputEqualsAFreshRun()
    {
        const string rules = """
            .ByNamespace(ns => ns.StartsWith("MyCompany.Shared"), Lifetime.Singleton, ConventionScope.AllReferences)
                        .ByNamespace(ns => ns.EndsWith(".Http"), Lifetime.Scoped, ConventionScope.AllReferences)
                        .ByTypeName((ns, name) => name.EndsWith("Clock"), Lifetime.Transient, ConventionScope.AllReferences)
            """;

        var runs = RunSequence(rules, SourceEdits);

        AssertEveryRunEqualsAFreshRun(runs);

        var shared = runs.Select(r => Package(r.Result, SharedName)).ToList();
        var sdk = runs.Select(r => Package(r.Result, SdkName)).ToList();

        Assert.All(shared, a => Assert.Same(shared[0], a));
        Assert.All(sdk, a => Assert.Same(sdk[0], a));

        var sharedWalk = TypeCollectorProbe.AssemblyWalk(shared[0]);
        var sdkWalk = TypeCollectorProbe.AssemblyWalk(sdk[0]);
        var prunedWalk = TypeCollectorProbe.NamespaceWalk(TypeCollectorProbe.Namespace(shared[0], "MyCompany.Shared"));
        Assert.NotNull(sharedWalk);
        Assert.NotNull(sdkWalk);
        Assert.NotNull(prunedWalk);
        for (var i = 1; i < runs.Count; i++)
        {
            Assert.Same(sharedWalk, TypeCollectorProbe.AssemblyWalk(shared[i]));
            Assert.Same(sdkWalk, TypeCollectorProbe.AssemblyWalk(sdk[i]));
            Assert.Same(prunedWalk, TypeCollectorProbe.NamespaceWalk(TypeCollectorProbe.Namespace(shared[i], "MyCompany.Shared")));
        }

        var before = Generated(runs[2].Result, ConventionDispatch.ByNamespaceFile);
        var after = Generated(runs[3].Result, ConventionDispatch.ByNamespaceFile);
        Assert.Contains(Registration("Singleton", IClock, SystemClock), before);
        Assert.DoesNotContain("ExtraClock", before);
        Assert.Contains(Registration("Singleton", IClock, SystemClock), after);
        Assert.Contains(Registration("Singleton", IClock, "MyCompany.Shared.Local.ExtraClock"), after);
        Assert.Contains(Registration("Transient", IClock, "MyCompany.Shared.Local.ExtraClock"), Generated(runs[3].Result, ConventionDispatch.ByTypeNameFile));
    }

    [Fact]
    public void SourceEdits_PrunedRuleNeverWalksAWholePackage()
    {
        var runs = RunSequence(
            """.ByNamespace(ns => ns.StartsWith("MyCompany.Shared"), Lifetime.Singleton, ConventionScope.AllReferences)""",
            SourceEdits);

        AssertEveryRunEqualsAFreshRun(runs);

        foreach (var (_, result) in runs)
        {
            Assert.False(TypeCollectorProbe.IsAssemblyWalked(Package(result, SharedName)));
            Assert.False(TypeCollectorProbe.IsAssemblyWalked(Package(result, SdkName)));
            Assert.NotEmpty(TypeCollectorProbe.WalkedNamespaces(Package(result, SharedName)));
            Assert.All(TypeCollectorProbe.WalkedNamespaces(Package(result, SharedName)), n => Assert.StartsWith("MyCompany.Shared", n, StringComparison.Ordinal));
            Assert.All(TypeCollectorProbe.WalkedNamespaces(Package(result, SdkName)), n => Assert.StartsWith("MyCompany.Shared", n, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ScopeEdits_OutputFollowsTheScope_AndEqualsAFreshRun()
    {
        static string Rules(string scopeArgument) => $$"""
            .ByNamespace(ns => ns.StartsWith("MyCompany.Shared"), Lifetime.Singleton{{scopeArgument}})
                        .ByTypeName((ns, name) => name.EndsWith("Clock"), Lifetime.Singleton{{scopeArgument}})
            """;

        string[] scopes = ["", ", ConventionScope.AllReferences", ", scope: ConventionScope.Compilation", ", scope: ConventionScope.AllReferences", ", ConventionScope.Compilation", ""];

        var runs = RunSequence(Rules(scopes[0]), scopes.Skip(1).Select(s => SetRules(Rules(s))));

        AssertEveryRunEqualsAFreshRun(runs);

        for (var i = 0; i < runs.Count; i++)
        {
            var allReferences = scopes[i].Contains("AllReferences", StringComparison.Ordinal);
            foreach (var file in new[] { ConventionDispatch.ByNamespaceFile, ConventionDispatch.ByTypeNameFile })
            {
                var branch = Assert.Single(ConventionDispatch.Branches(Generated(runs[i].Result, file)));
                Assert.Equal(allReferences ? "AllReferences" : "Compilation", branch.Scope);

                var expected = new List<string> { Registration("Singleton", "MyCompany.Shared.Local.ILocalClock", "MyCompany.Shared.Local.LocalClock") };
                if (allReferences)
                    expected.AddRange([Registration("Singleton", IClock, "MyCompany.Shared.Local.LocalClock"), Registration("Singleton", IClock, SystemClock)]);
                Assert.Equal(expected.OrderBy(r => r, StringComparer.Ordinal), branch.Registrations);
            }
        }

        string AllText(int run) => GeneratorOutputAssert.AllGeneratedText(runs[run].Result);
        Assert.Equal(AllText(0), AllText(2));
        Assert.Equal(AllText(0), AllText(4));
        Assert.Equal(AllText(0), AllText(5));
        Assert.Equal(AllText(1), AllText(3));
    }

    [Fact]
    public void PredicateEdits_SwitchBetweenPrunedAndFullWalks_AndOutputEqualsAFreshRun()
    {
        static string Rules(string predicate) => $".ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)";

        string[] predicates =
        [
            """ns => ns.StartsWith("MyCompany.Shared.Clock")""",
            """ns => ns.StartsWith("MyCompany.Sdk")""",
            """ns => ns.EndsWith(".Http") || ns.EndsWith(".Clock")""",
            """ns => ns.StartsWith(["MyCompany.Shared.Clock", "MyCompany.Sdk"])""",
            """ns => ns.StartsWith("MyCompany.Shared.Clock")""",
        ];

        var wholeWalks = new List<(bool Shared, bool Sdk)>();
        (bool, bool) Observe(Compilation c) => (
            TypeCollectorProbe.IsAssemblyWalked(TypeCollectorProbe.ReferencedAssembly(c, SharedName)),
            TypeCollectorProbe.IsAssemblyWalked(TypeCollectorProbe.ReferencedAssembly(c, SdkName)));

        var runs = RunSequence(
            Rules(predicates[0]),
            predicates.Skip(1).Select(p => (Func<Compilation, Compilation>)(c =>
            {
                wholeWalks.Add(Observe(c));
                return SetRules(Rules(p))(c);
            })));
        wholeWalks.Add(Observe(runs[^1].Compilation));

        AssertEveryRunEqualsAFreshRun(runs);

        Assert.All(runs, r => Assert.Same(Package(runs[0].Result, SharedName), Package(r.Result, SharedName)));
        Assert.Equal([(false, false), (false, false), (true, true), (true, true), (true, true)], wholeWalks);

        string[] Registered(int run) => [.. Assert.Single(ConventionDispatch.Branches(Generated(runs[run].Result, ConventionDispatch.ByNamespaceFile))).Registrations];

        var everything = new[] { Registration("Scoped", IHttpGateway, HttpGateway), Registration("Scoped", IClock, SystemClock) };
        Assert.Equal([Registration("Scoped", IClock, SystemClock)], Registered(0));
        Assert.Equal([Registration("Scoped", IHttpGateway, HttpGateway)], Registered(1));
        Assert.Equal(everything, Registered(2));
        Assert.Equal(everything, Registered(3));
        Assert.Equal(Registered(0), Registered(4));

        Assert.Equal(GeneratorOutputAssert.AllGeneratedText(runs[0].Result), GeneratorOutputAssert.AllGeneratedText(runs[4].Result));
    }

    [Fact]
    public void PrunedPredicateEdits_NeverWalkAWholePackage()
    {
        static string Rules(string predicate) => $".ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)";

        string[] predicates =
        [
            """ns => ns.StartsWith("MyCompany.Shared.Clock")""",
            """ns => ns.StartsWith("MyCompany.Sdk")""",
            """ns => ns == "MyCompany.Sdk.Http" || ns.StartsWith("MyCompany.Shared")""",
        ];

        var runs = RunSequence(Rules(predicates[0]), predicates.Skip(1).Select(p => SetRules(Rules(p))));

        AssertEveryRunEqualsAFreshRun(runs);
        Assert.False(TypeCollectorProbe.IsAssemblyWalked(Package(runs[^1].Result, SharedName)));
        Assert.False(TypeCollectorProbe.IsAssemblyWalked(Package(runs[^1].Result, SdkName)));
    }

    [Fact]
    public void ScopeEdit_ChangesOnlyTheEditedRule_WhichCarriesTheNewScope()
    {
        const string before = """
            .ByNamespace(ns => ns.StartsWith("MyCompany.Shared"), Lifetime.Singleton)
                        .ByTypeName((ns, name) => name.EndsWith("Clock"), Lifetime.Singleton)
            """;
        var after = before.Replace("Lifetime.Singleton)\n", "Lifetime.Singleton, ConventionScope.AllReferences)\n", StringComparison.Ordinal);
        Assert.NotEqual(before, after);

        var runs = RunSequence(before, [SetRules(after)]);
        var run = new IncrementalRun(runs[0].Result, runs[1].Result, runs[0].Compilation, runs[1].Compilation);

        var initialRule = Assert.IsType<NamespaceConventionRule>(Assert.Single(run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceRules)));
        var editedRule = Assert.IsType<NamespaceConventionRule>(Assert.Single(run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceRules)));
        Assert.Equal(ConventionScopes.Compilation, initialRule.Scope);
        Assert.Equal(ConventionScopes.AllReferences, editedRule.Scope);
        Assert.Equal(initialRule with { Scope = ConventionScopes.AllReferences }, editedRule);
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceRules));

        var typeNameRule = Assert.IsType<TypeNameConventionRule>(Assert.Single(run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameRules)));
        Assert.Equal(ConventionScopes.Compilation, typeNameRule.Scope);
        Assert.DoesNotContain(IncrementalStepRunReason.Modified, run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameRules));
        Assert.DoesNotContain(IncrementalStepRunReason.New, run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameRules));
    }

    [Fact]
    public void Svcfy017_FollowsScopeAndPredicateEdits_AndEqualsAFreshRun()
    {
        string[] rules =
        [
            ".ByNamespace(_ => true, Lifetime.Scoped)",
            ".ByNamespace(_ => true, Lifetime.Scoped, ConventionScope.AllReferences)",
            """.ByNamespace(ns => ns.StartsWith("MyCompany."), Lifetime.Scoped, ConventionScope.AllReferences)""",
            """.ByNamespace(ns => ns.StartsWith("My"), Lifetime.Scoped, ConventionScope.AllReferences)""",
            """.ByNamespace(ns => ns.StartsWith("My"), Lifetime.Scoped)""",
        ];
        int[] expectedWarnings = [0, 1, 0, 1, 0];

        var runs = RunSequence(rules[0], rules.Skip(1).Select(SetRules));

        AssertEveryRunEqualsAFreshRun(runs);
        for (var i = 0; i < runs.Count; i++)
            Assert.Equal(expectedWarnings[i], runs[i].Result.Diagnostics.Count(d => d.Id == "SVCFY017"));
    }

    [Fact]
    public void ReplacingTransitivePackage_NewVersionIsWalked_AndOutputFollowsIt()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13);
        var references = PackageCompilationHelper.BuildReferences(parseOptions, Packages());
        var v1 = references.Single(r => r.Display!.Contains("/mycompany.shared/", StringComparison.Ordinal));
        var v2 = PackageCompilationHelper.BuildReferences(
                parseOptions,
                [
                    SharedPackage(
                        """
                        namespace MyCompany.Shared.Clock
                        {
                            public class MonotonicClock : IClock { public string Name => "monotonic"; }
                        }
                        """,
                        "/home/test/.nuget/packages/mycompany.shared/1.1.0/lib/net8.0/MyCompany.Shared.dll"),
                ])
            .Last();

        const string rules = """
            .ByNamespace(ns => ns.StartsWith("MyCompany.Shared"), Lifetime.Singleton, ConventionScope.AllReferences)
                        .ByTypeName((ns, name) => name.EndsWith("Clock"), Lifetime.Transient, ConventionScope.AllReferences)
            """;

        var runs = IncrementalGeneratorHelper.RunSequence(
            PackageCompilationHelper.CreateCompilation(Consumer(Types, rules), references),
            [CompilationEdits.ReplaceReference(v1, v2), CompilationEdits.ReplaceReference(v2, v1)]);

        AssertEveryRunEqualsAFreshRun(runs);

        var oldShared = Package(runs[0].Result, SharedName);
        var newShared = Package(runs[1].Result, SharedName);
        Assert.NotSame(oldShared, newShared);
        Assert.NotNull(TypeCollectorProbe.NamespaceWalk(TypeCollectorProbe.Namespace(newShared, "MyCompany.Shared")));
        Assert.NotNull(TypeCollectorProbe.AssemblyWalk(newShared));

        var monotonic = Registration("Singleton", IClock, "MyCompany.Shared.Clock.MonotonicClock");
        Assert.DoesNotContain(monotonic, Generated(runs[0].Result, ConventionDispatch.ByNamespaceFile));
        Assert.Contains(monotonic, Generated(runs[1].Result, ConventionDispatch.ByNamespaceFile));
        Assert.Contains(Registration("Transient", IClock, "MyCompany.Shared.Clock.MonotonicClock"), Generated(runs[1].Result, ConventionDispatch.ByTypeNameFile));

        Assert.Equal(GeneratorOutputAssert.AllGeneratedText(runs[0].Result), GeneratorOutputAssert.AllGeneratedText(runs[2].Result));
    }
}
