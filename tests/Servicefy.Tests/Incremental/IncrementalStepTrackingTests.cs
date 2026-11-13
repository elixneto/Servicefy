using System.Reflection;
using Microsoft.CodeAnalysis;
using Servicefy.Package;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Incremental;

/// <summary>
/// Baseline checks for the incremental-step harness (<see cref="IncrementalGeneratorHelper"/>) that apply to
/// both generators: every pipeline step is named and tracked, a first run reports everything as new, and
/// edits that do not touch any DI registration leave the generated text identical (so any output that
/// re-runs on such an edit is wasted work).
/// </summary>
public class IncrementalStepTrackingTests
{
    private static readonly string[] AllTrackingNames = typeof(TrackingNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    private static readonly string[] ByConfigurationTrackingNames =
    [
        TrackingNames.ByConfigurationAttributesInput,
        TrackingNames.ByConfigurationAnnotatedClasses,
        TrackingNames.ByConfigurationDiagnosticsInput,
        TrackingNames.ByConfigurationRegistrationsInput,
    ];

    private static readonly string[] ConventionsTrackingNames =
    [
        TrackingNames.ConventionsBuilderInput,
        TrackingNames.ByNamespaceResults, TrackingNames.ByNamespaceRules, TrackingNames.ByNamespaceEmitInput,
        TrackingNames.ByBaseTypeResults, TrackingNames.ByBaseTypeRules, TrackingNames.ByBaseTypeEmitInput,
        TrackingNames.ByNamespaceOfResults, TrackingNames.ByNamespaceOfRules, TrackingNames.ByNamespaceOfEmitInput,
        TrackingNames.ByTypeNameResults, TrackingNames.ByTypeNameRules, TrackingNames.ByTypeNameEmitInput,
    ];

    [Fact]
    public void TrackingNames_AreUnique_AndEachBelongsToExactlyOneGenerator()
    {
        Assert.Equal(AllTrackingNames.Length, AllTrackingNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            AllTrackingNames.OrderBy(n => n, StringComparer.Ordinal),
            ByConfigurationTrackingNames.Concat(ConventionsTrackingNames).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void InitialRun_TestProject_GeneratesWithoutDiagnostics()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        Assert.Empty(run.Initial.Diagnostics);
        Assert.All(run.Initial.Results, r => Assert.Null(r.Exception));

        // Every convention call in the project resolves to a rule that matches at least one type,
        // so each registration emitter produces its file — the outputs the edits below re-run.
        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.g.cs"));
        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByNamespace.g.cs"));
        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"));
        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByNamespaceOf.g.cs"));
        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByTypeName.g.cs"));

        Assert.NotNull(run.InitialGeneratedSource<ServicefyByConfigurationGenerator>("AddScopedAttribute.g.cs"));
        Assert.Contains(
            run.InitialResult<ServicefyByConfigurationGenerator>().GeneratedSources,
            s => s.HintName.StartsWith("ServicefyExtensions.", StringComparison.Ordinal)
                 && s.SourceText.ToString().Contains("global::TestAssembly.Services.Greeter"));
    }

    [Fact]
    public void InitialRun_EveryNamedStep_IsTrackedAndNew()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        foreach (var name in ByConfigurationTrackingNames)
            AssertAllReasons(IncrementalStepRunReason.New, run.InitialReasons<ServicefyByConfigurationGenerator>(name), name);

        foreach (var name in ConventionsTrackingNames)
            AssertAllReasons(IncrementalStepRunReason.New, run.InitialReasons<ServicefyConventionsGenerator>(name), name);
    }

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void NonDiEdit_GeneratedSources_AreIdentical(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        Assert.Equal(
            IncrementalRun.GeneratedSources(run.InitialResult<ServicefyByConfigurationGenerator>()),
            IncrementalRun.GeneratedSources(run.EditedResult<ServicefyByConfigurationGenerator>()));
        Assert.Equal(
            IncrementalRun.GeneratedSources(run.InitialResult<ServicefyConventionsGenerator>()),
            IncrementalRun.GeneratedSources(run.EditedResult<ServicefyConventionsGenerator>()));
        Assert.Empty(run.Edited.Diagnostics);
    }

    internal static void AssertAllReasons(
        IncrementalStepRunReason expected, IReadOnlyList<IncrementalStepRunReason> actual, string what)
    {
        Assert.True(actual.Count > 0, $"'{what}' has no tracked outputs.");
        Assert.True(
            actual.All(r => r == expected),
            $"'{what}': expected every output to be {expected}, got [{string.Join(", ", actual)}].");
    }

    /// <summary>
    /// Asserts a <c>RegisterSourceOutput</c> step executed its action. For output steps Roslyn reports
    /// <see cref="IncrementalStepRunReason.Cached"/> when the action was skipped, and
    /// <see cref="IncrementalStepRunReason.Modified"/> / <see cref="IncrementalStepRunReason.Unchanged"/>
    /// when it ran and its (sources, diagnostics) result differs from / equals the previous one — an
    /// output that produces nothing (e.g. a diagnostics-only output with no diagnostics) re-runs as Unchanged.
    /// </summary>
    internal static void AssertReRan(IReadOnlyList<IncrementalStepRunReason> actual, string what)
    {
        Assert.True(actual.Count > 0, $"'{what}' has no tracked outputs.");
        Assert.True(
            actual.All(r => r is IncrementalStepRunReason.Modified or IncrementalStepRunReason.Unchanged),
            $"'{what}': expected every output to have re-run (Modified/Unchanged), got [{string.Join(", ", actual)}].");
    }
}

/// <summary>
/// Edits that change the compilation without changing any DI registration: a method body in a file
/// with no DI code, a brand-new file with no DI code, and a comment in the file holding the
/// convention calls.
/// </summary>
public static class NonDiEdits
{
    public const string MethodBodyInUnrelatedFile = "method body in unrelated file";
    public const string NewUnrelatedFile = "new unrelated file";
    public const string CommentInConventionsFile = "comment in conventions file";

    public static TheoryData<string> Names =>
    [
        MethodBodyInUnrelatedFile,
        NewUnrelatedFile,
        CommentInConventionsFile,
    ];

    internal static Func<Compilation, Compilation> Get(string name) => name switch
    {
        MethodBodyInUnrelatedFile => CompilationEdits.ReplaceText(
            IncrementalTestProject.UtilitiesPath, "return a + b;", "return b + a;"),
        NewUnrelatedFile => CompilationEdits.AddSource(
            "Dto.cs", "namespace TestAssembly.Dtos { public class OrderDto { public int Id { get; set; } } }"),
        CommentInConventionsFile => CompilationEdits.ReplaceText(
            IncrementalTestProject.StartupPath, "public class Startup", "// Composition root\n    public class Startup"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };
}
