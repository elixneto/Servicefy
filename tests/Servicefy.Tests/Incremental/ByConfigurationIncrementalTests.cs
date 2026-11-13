using Microsoft.CodeAnalysis;
using Servicefy.Package;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Incremental.IncrementalStepTrackingTests;

namespace Servicefy.Tests.Incremental;

/// <summary>
/// Documents the <b>current</b> incremental behavior of <see cref="ServicefyByConfigurationGenerator"/>
/// (issue #4). All three of its source outputs are built on <c>CompilationProvider</c>, so any edit to
/// the compilation — even one unrelated to DI — re-runs every output. The "Modified" assertions on the
/// compilation-based steps and outputs are expected to flip to Cached/Unchanged once the pipeline stops
/// depending on the whole compilation (#5 / #6).
/// </summary>
public class ByConfigurationIncrementalTests
{
    private static readonly string[] CompilationBasedSteps =
    [
        TrackingNames.ByConfigurationAttributesInput,
        TrackingNames.ByConfigurationDiagnosticsInput,
        TrackingNames.ByConfigurationRegistrationsInput,
    ];

    // ── Edits that do not change any registration ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void NonDiEdit_CompilationBasedSteps_AreModified(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        foreach (var name in CompilationBasedSteps)
            AssertAllReasons(IncrementalStepRunReason.Modified, run.EditedReasons<ServicefyByConfigurationGenerator>(name), name);
    }

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void NonDiEdit_AnnotatedClassesInUntouchedFile_AreCached(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        // The only annotated class (Greeter) lives in Services.cs, which none of these edits touch:
        // the syntax-only step keeps its previous value.
        Assert.Equal(
            [IncrementalStepRunReason.Cached],
            run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationAnnotatedClasses));
    }

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void NonDiEdit_EverySourceOutput_ReRuns(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        // Attributes, diagnostics and registrations: one RegisterSourceOutput each, none skipped.
        Assert.Equal(3, run.EditedSourceOutputSteps<ServicefyByConfigurationGenerator>().Length);
        AssertReRan(
            IncrementalRun.Reasons(run.EditedSourceOutputSteps<ServicefyByConfigurationGenerator>()),
            "ByConfiguration source outputs");

        // The two outputs that add sources produce new (though textually identical) source arrays.
        AssertAllReasons(
            IncrementalStepRunReason.Modified,
            run.EditedSourceOutputReasonsFedBy<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationAttributesInput),
            "attributes output");
        AssertAllReasons(
            IncrementalStepRunReason.Modified,
            run.EditedSourceOutputReasonsFedBy<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationRegistrationsInput),
            "registrations output");

        // The diagnostics output re-runs too; it reports nothing either time, hence Unchanged (not Cached).
        Assert.Equal(
            [IncrementalStepRunReason.Unchanged],
            run.EditedSourceOutputReasonsFedBy<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDiagnosticsInput));
    }

    [Fact]
    public void RerunOverSameCompilation_EveryStepAndSourceOutput_IsCached()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        foreach (var name in CompilationBasedSteps.Append(TrackingNames.ByConfigurationAnnotatedClasses))
            AssertAllReasons(IncrementalStepRunReason.Cached, run.EditedReasons<ServicefyByConfigurationGenerator>(name), name);

        AssertAllReasons(
            IncrementalStepRunReason.Cached,
            IncrementalRun.Reasons(run.EditedSourceOutputSteps<ServicefyByConfigurationGenerator>()),
            "ByConfiguration source outputs");
    }

    // ── Edits to DI-related code ──────────────────────────────────────────────────────────────

    [Fact]
    public void EditInsideAnnotatedClass_AnnotatedClasses_IsModified_ButRegistrationsAreIdentical()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.ReplaceText(
                IncrementalTestProject.ServicesPath,
                "public class Greeter : IGreeter { }",
                "public class Greeter : IGreeter { public string Greet() => \"hi\"; }"));

        // Collected values are ClassDeclarationSyntax nodes, compared by reference: any edit to the
        // file holding them yields new nodes, so the step reports Modified even though the
        // registration it describes did not change.
        Assert.Equal(
            [IncrementalStepRunReason.Modified],
            run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationAnnotatedClasses));

        AssertAllReasons(
            IncrementalStepRunReason.Modified,
            run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDiagnosticsInput),
            TrackingNames.ByConfigurationDiagnosticsInput);
        AssertReRan(
            run.EditedSourceOutputReasonsFedBy<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDiagnosticsInput),
            "diagnostics output");

        Assert.Equal(
            IncrementalRun.GeneratedSources(run.InitialResult<ServicefyByConfigurationGenerator>()),
            IncrementalRun.GeneratedSources(run.EditedResult<ServicefyByConfigurationGenerator>()));
    }

    [Fact]
    public void AddingAnnotatedClass_AnnotatedClasses_ReportsNewEntry_AndRegistrationsChange()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.AddSource("Clock.cs", """
                namespace TestAssembly.Time
                {
                    public interface IClock { }

                    [AddSingleton]
                    public class Clock : IClock { }
                }
                """));

        var reasons = run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationAnnotatedClasses);
        Assert.Equal(2, reasons.Count);
        Assert.Single(reasons, IncrementalStepRunReason.Cached); // Greeter, in the untouched Services.cs
        Assert.Single(reasons, IncrementalStepRunReason.New);    // Clock, in the new file

        AssertAllReasons(
            IncrementalStepRunReason.Modified,
            run.EditedSourceOutputReasonsFedBy<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationRegistrationsInput),
            "registrations output");

        Assert.DoesNotContain("global::TestAssembly.Time.Clock", ExtensionsSource(run.InitialResult<ServicefyByConfigurationGenerator>()));
        Assert.Contains("global::TestAssembly.Time.Clock", ExtensionsSource(run.EditedResult<ServicefyByConfigurationGenerator>()));
    }

    [Fact]
    public void ChangingAttributeLifetime_RegistrationsChange()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.ReplaceText(IncrementalTestProject.ServicesPath, "[AddScoped]", "[AddTransient]"));

        Assert.Equal(
            [IncrementalStepRunReason.Modified],
            run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationAnnotatedClasses));

        foreach (var name in CompilationBasedSteps)
            AssertAllReasons(IncrementalStepRunReason.Modified, run.EditedReasons<ServicefyByConfigurationGenerator>(name), name);

        Assert.Contains("AddScoped<", ExtensionsSource(run.InitialResult<ServicefyByConfigurationGenerator>()));
        Assert.DoesNotContain("AddTransient<", ExtensionsSource(run.InitialResult<ServicefyByConfigurationGenerator>()));
        Assert.Contains("AddTransient<", ExtensionsSource(run.EditedResult<ServicefyByConfigurationGenerator>()));
        Assert.DoesNotContain("AddScoped<", ExtensionsSource(run.EditedResult<ServicefyByConfigurationGenerator>()));

        // The attribute sources do not depend on user code: re-generated, but identical.
        Assert.Equal(
            run.InitialGeneratedSource<ServicefyByConfigurationGenerator>("AddScopedAttribute.g.cs"),
            run.EditedGeneratedSource<ServicefyByConfigurationGenerator>("AddScopedAttribute.g.cs"));
    }

    private static string ExtensionsSource(GeneratorRunResult result) =>
        string.Concat(result.GeneratedSources
            .Where(s => s.HintName.StartsWith("ServicefyExtensions.", StringComparison.Ordinal))
            .Select(s => s.SourceText.ToString()));
}
