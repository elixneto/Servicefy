using System.Reflection;
using Microsoft.CodeAnalysis;
using Servicefy.Package;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Incremental;

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
        TrackingNames.ByConfigurationDecorateCalls,
    ];

    private static readonly string[] ConventionsTrackingNames =
    [
        TrackingNames.ConventionsBuilderInput,
        TrackingNames.ConventionsDecorateCalls, TrackingNames.ConventionsTypePool,
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

    private static readonly string[] DecorateCallTrackingNames =
    [
        TrackingNames.ByConfigurationDecorateCalls,
        TrackingNames.ConventionsDecorateCalls,
    ];

    [Fact]
    public void InitialRun_EveryNamedStep_IsTrackedAndNew()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        foreach (var name in ByConfigurationTrackingNames.Except(DecorateCallTrackingNames))
            AssertAllReasons(IncrementalStepRunReason.New, run.InitialReasons<ServicefyByConfigurationGenerator>(name), name);

        foreach (var name in ConventionsTrackingNames.Except(DecorateCallTrackingNames))
            AssertAllReasons(IncrementalStepRunReason.New, run.InitialReasons<ServicefyConventionsGenerator>(name), name);
    }

    [Fact]
    public void InitialRun_ProjectWithDecorators_EveryNamedStepIsTrackedAndNew()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(DecoratorTestProject.Sources, CompilationEdits.None);

        string[] noCallSites =
        [
            TrackingNames.ByNamespaceOfResults, TrackingNames.ByNamespaceOfRules,
            TrackingNames.ByTypeNameResults, TrackingNames.ByTypeNameRules,
        ];

        foreach (var name in ByConfigurationTrackingNames)
            AssertAllReasons(IncrementalStepRunReason.New, run.InitialReasons<ServicefyByConfigurationGenerator>(name), name);

        foreach (var name in ConventionsTrackingNames.Except(noCallSites))
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

    internal static void AssertReRan(IReadOnlyList<IncrementalStepRunReason> actual, string what)
    {
        Assert.True(actual.Count > 0, $"'{what}' has no tracked outputs.");
        Assert.True(
            actual.All(r => r is IncrementalStepRunReason.Modified or IncrementalStepRunReason.Unchanged),
            $"'{what}': expected every output to have re-run (Modified/Unchanged), got [{string.Join(", ", actual)}].");
    }
}

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
