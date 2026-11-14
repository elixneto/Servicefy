using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Servicefy.Package;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Incremental.IncrementalStepTrackingTests;

namespace Servicefy.Tests.Incremental;

public class ConventionDiagnosticsIncrementalTests
{
    private const string TypesPath = "Types.cs";
    private const string StartupPath = "Startup.cs";
    private const string UtilitiesPath = "Utilities.cs";

    private const string Types = """
        namespace TestAssembly
        {
            public interface IFoo { }
            public class Foo : IFoo { }
        }
        """;

    private const string Startup = """
        namespace TestAssembly
        {
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped, ServiceTypeSelector.Self);
                    services.AddServicefyConventions().ByBaseType(typeof(IFoo), Lifetime.Transient);
                    services.AddServicefyConventions().ByNamespace(ns => ns.Length > 3, Lifetime.Scoped);
                    services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith(["Service", ns]), Lifetime.Scoped);
                }
            }
        }
        """;

    private const string Utilities = """
        namespace TestAssembly.Utilities
        {
            public static class MathUtil
            {
                public static int Add(int a, int b)
                {
                    return a + b;
                }
            }
        }
        """;

    private static IEnumerable<(string Path, string Source)> Sources =>
    [
        (TypesPath, Types),
        (StartupPath, Startup),
        (UtilitiesPath, Utilities),
    ];

    private static readonly (string Id, string Anchor, string Target)[] ExpectedDiagnostics =
    [
        ("SVCFY010", "ByBaseType<IFoo>(Lifetime.Scoped, ServiceTypeSelector.Self)", "IFoo"),
        ("SVCFY016", "typeof(IFoo)", "IFoo"),
        ("SVCFY012", "ns => ns.Length > 3", "ns.Length > 3"),
        ("SVCFY011", "[\"Service\", ns]", "ns"),
    ];

    [Fact]
    public void InitialRun_EveryCallSiteDiagnostic_PointsAtItsCallSite()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.None);

        AssertDiagnosticsAt(run.Initial.Diagnostics, Startup);
    }

    [Theory]
    [MemberData(nameof(UnrelatedEdits))]
    public void UnrelatedEdit_CallSitesUnchanged_DiagnosticsOutputsCached_AndDiagnosticsStillReported(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetUnrelatedEdit(edit));

        foreach (var name in new[]
                 {
                     TrackingNames.ByBaseTypeResults, TrackingNames.ByNamespaceResults, TrackingNames.ByTypeNameResults,
                 })
            AssertAllReasons(IncrementalStepRunReason.Unchanged, run.EditedReasons<ServicefyConventionsGenerator>(name), name);

        var diagnosticsOutputs = run.EditedSourceOutputSteps<ServicefyConventionsGenerator>()
            .Where(step => step.Inputs.All(input => input.Source.Name is null))
            .ToList();
        Assert.Equal(4, diagnosticsOutputs.Count);
        AssertAllReasons(IncrementalStepRunReason.Cached, IncrementalRun.Reasons(diagnosticsOutputs), "call-site diagnostics outputs");

        AssertDiagnosticsAt(run.Edited.Diagnostics, Startup);
        Assert.Equal(Describe(run.Initial.Diagnostics), Describe(run.Edited.Diagnostics));
    }

    public static TheoryData<string> UnrelatedEdits => ["method body in unrelated file", "new unrelated file", "trailing comment in conventions file"];

    private static Func<Compilation, Compilation> GetUnrelatedEdit(string name) => name switch
    {
        "method body in unrelated file" => CompilationEdits.ReplaceText(UtilitiesPath, "return a + b;", "return b + a;"),
        "new unrelated file" => CompilationEdits.AddSource("Dto.cs", "namespace TestAssembly.Dtos { public class OrderDto { } }"),
        "trailing comment in conventions file" => CompilationEdits.InsertAfter(
            StartupPath, "name.EndsWith([\"Service\", ns]), Lifetime.Scoped);", "\n            // end of conventions"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Fact]
    public void LinesInsertedAboveCallSites_DiagnosticsMoveWithTheirCallSites()
    {
        const string header = "// Composition root\n// Conventions below are evaluated at build time.\n";
        var edited = header + Startup;

        var run = IncrementalGeneratorHelper.RunWithEdit(
            Sources, CompilationEdits.ReplaceSource(StartupPath, edited));

        Assert.Equal([IncrementalStepRunReason.Modified, IncrementalStepRunReason.Modified],
            run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeResults));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceResults));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameResults));

        AssertDiagnosticsAt(run.Initial.Diagnostics, Startup);
        AssertDiagnosticsAt(run.Edited.Diagnostics, edited);

        foreach (var id in ExpectedDiagnostics.Select(e => e.Id))
        {
            var before = run.Initial.Diagnostics.Single(d => d.Id == id).Location.GetLineSpan().Span;
            var after = run.Edited.Diagnostics.Single(d => d.Id == id).Location.GetLineSpan().Span;
            Assert.Equal(before.Start.Line + 2, after.Start.Line);
            Assert.Equal(before.Start.Character, after.Start.Character);
            Assert.Equal(before.End.Line + 2, after.End.Line);
            Assert.Equal(before.End.Character, after.End.Character);
        }
    }

    [Fact]
    public void FixingUnsupportedPredicate_RemovesItsDiagnostic_AndTheOthersStayAtTheirCallSites()
    {
        var edited = Startup.Replace("ns => ns.Length > 3", "ns => ns.StartsWith(\"TestAssembly\")", StringComparison.Ordinal);

        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.ReplaceSource(StartupPath, edited));

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceResults));
        Assert.Equal([IncrementalStepRunReason.New], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceRules));

        Assert.Contains(run.Initial.Diagnostics, d => d.Id == "SVCFY012");
        Assert.DoesNotContain(run.Edited.Diagnostics, d => d.Id == "SVCFY012");

        AssertDiagnosticsAt(run.Edited.Diagnostics, edited, ExpectedDiagnostics.Where(e => e.Id != "SVCFY012"));

        var byNamespace = run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByNamespace.g.cs");
        Assert.NotNull(byNamespace);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IFoo, global::TestAssembly.Foo>();", byNamespace);
    }

    [Fact]
    public void BreakingAPredicate_ReportsItsDiagnosticAtTheNewCallSite()
    {
        var edited = Startup.Replace(
            "services.AddServicefyConventions().ByTypeName(",
            "services.AddServicefyConventions().ByNamespace(ns => ns.Contains([\"Data\", ns]), Lifetime.Singleton);\n            services.AddServicefyConventions().ByTypeName(",
            StringComparison.Ordinal);

        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.ReplaceSource(StartupPath, edited));

        var reasons = run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceResults);
        Assert.Equal(2, reasons.Count);
        Assert.Single(reasons, IncrementalStepRunReason.Unchanged);
        Assert.Single(reasons, IncrementalStepRunReason.New);

        var svcfy011 = run.Edited.Diagnostics.Where(d => d.Id == "SVCFY011").OrderBy(d => d.Location.SourceSpan.Start).ToList();
        Assert.Equal(2, svcfy011.Count);
        AssertAt(svcfy011[0], edited, "[\"Data\", ns]", "ns");
        AssertAt(svcfy011[1], edited, "[\"Service\", ns]", "ns");
    }

    [Fact]
    public void DiagnosticsFromCallSites_KeepIdSeverityAndMessage()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.None);

        var svcfy010 = run.Initial.Diagnostics.Single(d => d.Id == "SVCFY010");
        Assert.Equal(DiagnosticSeverity.Error, svcfy010.Severity);
        Assert.Equal("ServiceRegistration", svcfy010.Descriptor.Category);
        Assert.StartsWith("'TestAssembly.IFoo' is an interface", svcfy010.GetMessage());

        var svcfy016 = run.Initial.Diagnostics.Single(d => d.Id == "SVCFY016");
        Assert.Equal(DiagnosticSeverity.Error, svcfy016.Severity);
        Assert.Equal("ServiceRegistration", svcfy016.Descriptor.Category);
        Assert.Equal("ByBaseType(Type) requires a generic type", svcfy016.Descriptor.Title.ToString());
        Assert.StartsWith("'TestAssembly.IFoo' is not a generic type.", svcfy016.GetMessage());

        var svcfy011 = run.Initial.Diagnostics.Single(d => d.Id == "SVCFY011");
        Assert.Equal(DiagnosticSeverity.Error, svcfy011.Severity);
        Assert.StartsWith("The array passed to 'EndsWith' must contain only string literals", svcfy011.GetMessage());

        var svcfy012 = run.Initial.Diagnostics.Single(d => d.Id == "SVCFY012");
        Assert.Equal(DiagnosticSeverity.Error, svcfy012.Severity);
        Assert.Equal("Unsupported predicate expression", svcfy012.Descriptor.Title.ToString());
    }

    [Fact]
    public void DiagnosticsInSeveralFiles_EachPointsAtItsOwnFile()
    {
        const string otherPath = "OtherStartup.cs";
        const string other = """
            namespace TestAssembly.Other
            {
                public class OtherStartup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(x => x.Length == 0, Lifetime.Scoped);
                    }
                }
            }
            """;

        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.AddSource(otherPath, other));

        var svcfy012 = run.Edited.Diagnostics.Where(d => d.Id == "SVCFY012").ToList();
        Assert.Equal(2, svcfy012.Count);
        AssertAt(svcfy012.Single(d => d.Location.GetLineSpan().Path == StartupPath), Startup, "ns => ns.Length > 3", "ns.Length > 3");
        AssertAt(svcfy012.Single(d => d.Location.GetLineSpan().Path == otherPath), other, "x => x.Length == 0", "x.Length == 0", otherPath);
    }

    private static void AssertDiagnosticsAt(
        IEnumerable<Diagnostic> diagnostics,
        string startupSource,
        IEnumerable<(string Id, string Anchor, string Target)>? expected = null)
    {
        var list = diagnostics.ToList();
        var expectedList = (expected ?? ExpectedDiagnostics).ToList();

        Assert.Equal(
            expectedList.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal),
            list.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal));

        foreach (var (id, anchor, target) in expectedList)
            AssertAt(list.Single(d => d.Id == id), startupSource, anchor, target);
    }

    private static void AssertAt(Diagnostic diagnostic, string source, string anchor, string target, string path = StartupPath)
    {
        var anchorIndex = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(anchorIndex >= 0 && source.IndexOf(anchor, anchorIndex + 1, StringComparison.Ordinal) < 0,
            $"Anchor '{anchor}' must occur exactly once.");
        var targetIndex = anchor.IndexOf(target, StringComparison.Ordinal);
        Assert.True(targetIndex >= 0, $"Target '{target}' must occur in anchor '{anchor}'.");

        var expectedSpan = new TextSpan(anchorIndex + targetIndex, target.Length);
        var expectedLineSpan = SourceText.From(source).Lines.GetLinePositionSpan(expectedSpan);

        var location = diagnostic.Location;
        Assert.NotEqual(Location.None, location);
        Assert.Equal(expectedSpan, location.SourceSpan);

        var lineSpan = location.GetLineSpan();
        Assert.Equal(path, lineSpan.Path);
        Assert.Equal(expectedLineSpan, lineSpan.Span);

        var mapped = location.GetMappedLineSpan();
        Assert.Equal(path, mapped.Path);
        Assert.Equal(expectedLineSpan, mapped.Span);
    }

    private static List<string> Describe(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics
            .Select(d => $"{d.Id}|{d.Severity}|{d.GetMessage()}|{d.Location.GetLineSpan()}|{d.Location.SourceSpan}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
}
