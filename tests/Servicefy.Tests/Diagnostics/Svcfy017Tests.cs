using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Helpers.ConventionScopeTestKit;

namespace Servicefy.Tests.Diagnostics;

public class Svcfy017Tests
{
    private const string Id = "SVCFY017";

    private static PackageRunResult RunRule(string rule) =>
        PackageCompilationHelper.Run(Consumer(types: "", rules: rule), Packages());

    private static Diagnostic AssertSingleSvcfy017(PackageRunResult result, string predicate)
    {
        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal(Id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.DefaultSeverity);

        Assert.Equal("Consumer.cs", diagnostic.Location.GetLineSpan().Path);
        Assert.Equal("Consumer.cs", diagnostic.Location.GetMappedLineSpan().Path);
        var consumer = result.Input!.SyntaxTrees.Single().GetText(TestContext.Current.CancellationToken);
        Assert.Equal(predicate, consumer.ToString(diagnostic.Location.SourceSpan));
        Assert.Equal(consumer.Lines.GetLinePositionSpan(diagnostic.Location.SourceSpan), diagnostic.Location.GetLineSpan().Span);

        var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(predicate, message);
        Assert.Contains("ConventionScope.AllReferences", message);
        return diagnostic;
    }

    public static TheoryData<string> BroadNamespacePredicates =>
    [
        "_ => true",
        "ns => true",
        "(ns) => true",
        "ns => (true)",
        """ns => ns.StartsWith("")""",
        """ns => ns.StartsWith("M")""",
        """ns => ns.StartsWith("MyC")""",
        """ns => ns.StartsWith("MyC", StringComparison.Ordinal)""",
        """ns => ns.StartsWith("myc", StringComparison.OrdinalIgnoreCase)""",
        """ns => ns.StartsWith(StringComparison.Ordinal, "MyCompany", "My")""",
        """ns => ns.StartsWith("MyCompany", "My")""",
        """ns => ns.StartsWith(["MyCompany", "My"])""",
        """ns => ns.StartsWith("My") && ns.EndsWith(".Clock")""",
        """ns => ns.StartsWith("MyCompany") || ns.StartsWith("My")""",
        """ns => (ns.StartsWith("My"))""",
    ];

    [Theory]
    [MemberData(nameof(BroadNamespacePredicates))]
    public void ByNamespace_BroadPredicateWithAllReferences_ReportsWarning_AndTheRuleIsStillApplied(string predicate)
    {
        var result = RunRule($".ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)");

        AssertSingleSvcfy017(result, predicate);

        Assert.Empty(result.CompilationErrors);
        var branch = ConventionDispatch.Branch(result.GeneratedSource(ConventionDispatch.ByNamespaceFile), "Scoped", "AllReferences", predicate);
        Assert.NotNull(branch);
        Assert.Contains(Registration("Scoped", IClock, SystemClock), branch.Registrations);
    }

    [Theory]
    [MemberData(nameof(BroadNamespacePredicates))]
    public void ByNamespace_BroadPredicateWithNamedScopeArgument_ReportsWarning(string predicate)
    {
        var result = RunRule($".ByNamespace({predicate}, Lifetime.Scoped, scope: ConventionScope.AllReferences)");

        AssertSingleSvcfy017(result, predicate);
    }

    public static TheoryData<string> NarrowNamespacePredicates =>
    [
        """ns => ns.StartsWith("MyCo")""",
        """ns => ns.StartsWith("MyCompany.")""",
        """ns => ns.StartsWith("mycompany", StringComparison.OrdinalIgnoreCase)""",
        """ns => ns.StartsWith(StringComparison.Ordinal, "MyCompany", "Vendor")""",
        """ns => ns.StartsWith("MyCompany", "Vendor")""",
        """ns => ns.StartsWith(["MyCompany", "Vendor"])""",
        """ns => ns.StartsWith("MyCompany") && ns.EndsWith(".Clock")""",
        """ns => ns.StartsWith("MyCompany.Shared") || ns.StartsWith("MyCompany.Sdk")""",
        """ns => ns.EndsWith(".Clock")""",
        """ns => ns.Contains("Shared")""",
        "ns => ns == \"MyCompany.Shared.Clock\"",
    ];

    [Theory]
    [MemberData(nameof(NarrowNamespacePredicates))]
    public void ByNamespace_NarrowPredicateWithAllReferences_IsNotReported(string predicate)
    {
        var result = RunRule($".ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)");

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationErrors);
    }

    [Theory]
    [InlineData("ns => ns == \"App\"")]
    [InlineData("""ns => ns.Equals("App")""")]
    [InlineData("""ns => "App" == ns""")]
    public void ByNamespace_ExactMatchOfAShortNamespace_IsNotReported(string predicate)
    {
        var result = RunRule($".ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)");

        var reported = result.GeneratorDiagnostics.Where(d => d.Id == Id).ToList();
        Assert.True(
            reported.Count == 0,
            $"SVCFY017 reported for the exact-match predicate '{predicate}': " +
            string.Join("; ", reported.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Theory]
    [MemberData(nameof(BroadNamespacePredicates))]
    public void ByNamespace_BroadPredicateWithCompilationScope_IsNotReported(string predicate)
    {
        foreach (var scopeArgument in new[] { "", ", ConventionScope.Compilation", ", scope: ConventionScope.Compilation" })
        {
            var result = RunRule($".ByNamespace({predicate}, Lifetime.Scoped{scopeArgument})");

            Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == Id);
        }
    }

    [Fact]
    public void ByNamespace_BroadPredicateWithUnevaluableScope_IsNotReported()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using TestAssembly;

            namespace TestAssembly.Composition
            {
                internal static class Startup
                {
                    internal static void Configure(IServiceCollection services, ConventionScope scope)
                    {
                        services.AddServicefyConventions().ByNamespace(_ => true, Lifetime.Scoped, scope);
                        services.AddServicefyConventions().ByTypeName((ns, name) => true, Lifetime.Scoped, scope);
                    }
                }
            }
            """;

        var result = PackageCompilationHelper.Run(source, Packages());

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == Id);
        Assert.Empty(result.CompilationErrors);
    }

    [Theory]
    [InlineData("(ns, name) => true")]
    [InlineData("(_, _) => true")]
    [InlineData("(ns, name) => (true)")]
    public void ByTypeName_MatchAllPredicateWithAllReferences_ReportsWarning_AndTheRuleIsStillApplied(string predicate)
    {
        var result = RunRule($".ByTypeName({predicate}, Lifetime.Singleton, ConventionScope.AllReferences)");

        AssertSingleSvcfy017(result, predicate);

        Assert.Empty(result.CompilationErrors);
        var branch = ConventionDispatch.Branch(result.GeneratedSource(ConventionDispatch.ByTypeNameFile), "Singleton", "AllReferences", predicate);
        Assert.NotNull(branch);
        Assert.Contains(Registration("Singleton", IClock, SystemClock), branch.Registrations);
        Assert.Contains(Registration("Singleton", IHttpGateway, HttpGateway), branch.Registrations);
    }

    [Theory]
    [InlineData("""(ns, name) => ns.StartsWith("M")""")]
    [InlineData("""(ns, name) => ns.StartsWith("")""")]
    [InlineData("""(ns, name) => name.StartsWith("S")""")]
    [InlineData("""(ns, name) => name.EndsWith("Clock")""")]
    [InlineData("""(ns, name) => ns.StartsWith("MyCompany") && name.EndsWith("Clock")""")]
    public void ByTypeName_PredicateThatIsNotMatchAll_IsNotReported(string predicate)
    {
        var result = RunRule($".ByTypeName({predicate}, Lifetime.Singleton, ConventionScope.AllReferences)");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == Id);
        Assert.Empty(result.CompilationErrors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(", ConventionScope.Compilation")]
    [InlineData(", scope: ConventionScope.Compilation")]
    public void ByTypeName_MatchAllPredicateWithCompilationScope_IsNotReported(string scopeArgument)
    {
        var result = RunRule($".ByTypeName((ns, name) => true, Lifetime.Singleton{scopeArgument})");

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == Id);
    }

    [Fact]
    public void EveryBroadCallSite_GetsItsOwnWarning_AtItsOwnPredicate()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using TestAssembly;

            namespace TestAssembly.Composition
            {
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(_ => true, Lifetime.Scoped, ConventionScope.AllReferences);
                        services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("MyCompany."), Lifetime.Scoped, ConventionScope.AllReferences);
                        services.AddServicefyConventions().ByTypeName((ns, name) => true, Lifetime.Transient, ConventionScope.AllReferences);
                        services.AddServicefyConventions().ByNamespace(_ => true, Lifetime.Scoped);
                        services.AddServicefyConventions().ByNamespace(nm => nm.StartsWith("My"), Lifetime.Singleton, ConventionScope.AllReferences);
                    }
                }
            }
            """;

        var result = PackageCompilationHelper.Run(source, Packages());

        Assert.Empty(result.CompilationErrors);
        Assert.All(result.GeneratorDiagnostics, d => Assert.Equal(Id, d.Id));

        var lines = source.Split('\n');
        int LineOf(string text) => Array.FindIndex(lines, l => l.Contains(text, StringComparison.Ordinal));

        var reported = result.GeneratorDiagnostics
            .Select(d => (
                Line: d.Location.GetLineSpan().StartLinePosition.Line,
                Column: d.Location.GetLineSpan().StartLinePosition.Character,
                Text: source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)))
            .OrderBy(d => d.Line)
            .ToList();

        var expected = new[]
            {
                (Line: LineOf("ByNamespace(_ => true, Lifetime.Scoped, ConventionScope.AllReferences)"), Text: "_ => true"),
                (Line: LineOf("ByTypeName((ns, name) => true"), Text: "(ns, name) => true"),
                (Line: LineOf("""nm.StartsWith("My")"""), Text: """nm => nm.StartsWith("My")"""),
            }
            .Select(e => (e.Line, Column: lines[e.Line].IndexOf(e.Text, StringComparison.Ordinal), e.Text))
            .ToList();

        Assert.Equal(expected, reported);
    }

    [Fact]
    public void BroadPredicateWithAllReferences_StillResolvesAtRuntime()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using TestAssembly;

            namespace TestAssembly
            {
                public static class Acceptance
                {
                    public static string Resolve()
                    {
                        var services = new ServiceCollection();
                        services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("My"), Lifetime.Singleton, ConventionScope.AllReferences);

                        using var provider = services.BuildServiceProvider();
                        return provider.GetRequiredService<MyCompany.Shared.Clock.IClock>().Name;
                    }
                }
            }
            """;

        Assert.Equal("system", PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Acceptance", "Resolve", Packages()));
    }
}
