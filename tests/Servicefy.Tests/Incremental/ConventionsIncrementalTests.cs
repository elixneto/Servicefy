using Microsoft.CodeAnalysis;
using Servicefy.Package;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Incremental.IncrementalStepTrackingTests;

namespace Servicefy.Tests.Incremental;

public class ConventionsIncrementalTests
{
    private static readonly string[] CompilationBasedSteps =
    [
        TrackingNames.ConventionsBuilderInput,
        TrackingNames.ByNamespaceEmitInput,
        TrackingNames.ByBaseTypeEmitInput,
        TrackingNames.ByNamespaceOfEmitInput,
        TrackingNames.ByTypeNameEmitInput,
    ];

    private static readonly string[] CallSiteSteps =
    [
        TrackingNames.ByNamespaceResults, TrackingNames.ByNamespaceRules,
        TrackingNames.ByBaseTypeResults, TrackingNames.ByBaseTypeRules,
        TrackingNames.ByNamespaceOfResults, TrackingNames.ByNamespaceOfRules,
        TrackingNames.ByTypeNameResults, TrackingNames.ByTypeNameRules,
    ];

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void NonDiEdit_BuilderInputAndEmitInputs_AreModified(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        foreach (var name in CompilationBasedSteps)
            AssertAllReasons(IncrementalStepRunReason.Modified, run.EditedReasons<ServicefyConventionsGenerator>(name), name);
    }

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void NonDiEdit_CallSiteResultsAndRules_AreModified(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        foreach (var name in CallSiteSteps)
            Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(name));
    }

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void NonDiEdit_EveryConventionSourceOutput_ReRuns(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        var outputs = run.EditedSourceOutputSteps<ServicefyConventionsGenerator>();
        Assert.Equal(9, outputs.Length);
        AssertReRan(IncrementalRun.Reasons(outputs), "Conventions source outputs");

        foreach (var name in CompilationBasedSteps)
            AssertAllReasons(
                IncrementalStepRunReason.Modified,
                run.EditedSourceOutputReasonsFedBy<ServicefyConventionsGenerator>(name),
                $"source output fed by {name}");

        var diagnosticsOutputs = outputs
            .Where(step => step.Inputs.All(input => input.Source.Name is null))
            .ToList();
        Assert.Equal(4, diagnosticsOutputs.Count);
        AssertAllReasons(IncrementalStepRunReason.Unchanged, IncrementalRun.Reasons(diagnosticsOutputs), "call-site diagnostics outputs");
    }

    [Fact]
    public void RerunOverSameCompilation_BuilderAndByBaseType_AreCached()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsBuilderInput));
        Assert.Equal(
            [IncrementalStepRunReason.Cached],
            run.EditedSourceOutputReasonsFedBy<ServicefyConventionsGenerator>(TrackingNames.ConventionsBuilderInput));

        Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeResults));
        Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules));
        Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeEmitInput));
        Assert.Equal(
            [IncrementalStepRunReason.Cached],
            run.EditedSourceOutputReasonsFedBy<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeEmitInput));
    }

    [Theory]
    [InlineData(TrackingNames.ByNamespaceResults, TrackingNames.ByNamespaceRules, TrackingNames.ByNamespaceEmitInput)]
    [InlineData(TrackingNames.ByNamespaceOfResults, TrackingNames.ByNamespaceOfRules, TrackingNames.ByNamespaceOfEmitInput)]
    [InlineData(TrackingNames.ByTypeNameResults, TrackingNames.ByTypeNameRules, TrackingNames.ByTypeNameEmitInput)]
    public void RerunOverSameCompilation_PredicateConventions_StillReRunTheirEmitter(
        string results, string rules, string emitInput)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(results));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(rules));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(emitInput));
        AssertAllReasons(
            IncrementalStepRunReason.Modified,
            run.EditedSourceOutputReasonsFedBy<ServicefyConventionsGenerator>(emitInput),
            $"source output fed by {emitInput}");
    }

    [Fact]
    public void ChangingByNamespaceLifetime_ByNamespaceStepsModified_AndRegistrationsChange()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.ReplaceText(
                IncrementalTestProject.StartupPath,
                "ns.StartsWith(\"TestAssembly.Repositories\"), Lifetime.Scoped",
                "ns.StartsWith(\"TestAssembly.Repositories\"), Lifetime.Singleton"));

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceResults));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceRules));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceEmitInput));
        AssertAllReasons(
            IncrementalStepRunReason.Modified,
            run.EditedSourceOutputReasonsFedBy<ServicefyConventionsGenerator>(TrackingNames.ByNamespaceEmitInput),
            "ByNamespace registrations output");

        const string hint = "ServicefyConventionsBuilder.ByNamespace.g.cs";
        var before = run.InitialGeneratedSource<ServicefyConventionsGenerator>(hint);
        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>(hint);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Repositories.IOrderRepository, global::TestAssembly.Repositories.OrderRepository>();", before);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.Repositories.IOrderRepository, global::TestAssembly.Repositories.OrderRepository>();", after);
        Assert.DoesNotContain("Services.AddScoped<", after);

        foreach (var other in new[]
                 {
                     "ServicefyConventionsBuilder.ByBaseType.g.cs",
                     "ServicefyConventionsBuilder.ByNamespaceOf.g.cs",
                     "ServicefyConventionsBuilder.ByTypeName.g.cs",
                 })
            Assert.Equal(run.InitialGeneratedSource<ServicefyConventionsGenerator>(other), run.EditedGeneratedSource<ServicefyConventionsGenerator>(other));
    }

    [Fact]
    public void AddingByBaseTypeCall_ResultsReportNewEntry_AndRegistrationsChange()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.AddSource("MoreStartup.cs", """
                namespace TestAssembly.Validation
                {
                    public interface IValidator { }
                    public class OrderValidator : IValidator { }

                    public class MoreStartup
                    {
                        public void Configure(IServiceCollection services)
                        {
                            services.AddServicefyConventions().ByBaseType<IValidator>(Lifetime.Singleton);
                        }
                    }
                }
                """));

        var reasons = run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeResults);
        Assert.Equal(2, reasons.Count);
        Assert.Single(reasons, IncrementalStepRunReason.Modified);
        Assert.Single(reasons, IncrementalStepRunReason.New);

        var rules = run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules);
        Assert.Equal(2, rules.Count);
        Assert.Single(rules, IncrementalStepRunReason.Modified);
        Assert.Single(rules, IncrementalStepRunReason.New);

        const string hint = "ServicefyConventionsBuilder.ByBaseType.g.cs";
        Assert.DoesNotContain("global::TestAssembly.Validation.OrderValidator", run.InitialGeneratedSource<ServicefyConventionsGenerator>(hint));
        Assert.Contains(
            "Services.AddSingleton<global::TestAssembly.Validation.IValidator, global::TestAssembly.Validation.OrderValidator>();",
            run.EditedGeneratedSource<ServicefyConventionsGenerator>(hint));
    }

    [Fact]
    public void RemovingByTypeNameCall_ResultsAndRulesReportRemoved_AndRegistrationsDisappear()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.ReplaceText(
                IncrementalTestProject.StartupPath,
                "services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith(\"Repository\"), Lifetime.Scoped);",
                ""));

        Assert.Equal([IncrementalStepRunReason.Removed], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameResults));
        Assert.Equal([IncrementalStepRunReason.Removed], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameRules));

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameEmitInput));

        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByTypeName.g.cs"));
        Assert.Null(run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByTypeName.g.cs"));
    }

    [Fact]
    public void AddingTypeMatchedByConvention_UntouchedCallSitesStillModified_AndRegistrationsIncludeIt()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.AddSource("PaymentRepository.cs", """
                namespace TestAssembly.Repositories
                {
                    public interface IPaymentRepository { }
                    public class PaymentRepository : IPaymentRepository { }
                }
                """));

        foreach (var name in CallSiteSteps)
            Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(name));

        foreach (var hint in new[]
                 {
                     "ServicefyConventionsBuilder.ByNamespace.g.cs",
                     "ServicefyConventionsBuilder.ByNamespaceOf.g.cs",
                     "ServicefyConventionsBuilder.ByTypeName.g.cs",
                 })
        {
            Assert.DoesNotContain("global::TestAssembly.Repositories.PaymentRepository", run.InitialGeneratedSource<ServicefyConventionsGenerator>(hint));
            Assert.Contains(
                "<global::TestAssembly.Repositories.IPaymentRepository, global::TestAssembly.Repositories.PaymentRepository>();",
                run.EditedGeneratedSource<ServicefyConventionsGenerator>(hint));
        }

        Assert.Equal(
            run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"),
            run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"));
    }
}
