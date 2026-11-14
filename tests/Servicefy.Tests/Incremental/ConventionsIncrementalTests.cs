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
        TrackingNames.ConventionsTypePool,
        TrackingNames.ByNamespaceEmitInput,
        TrackingNames.ByBaseTypeEmitInput,
        TrackingNames.ByNamespaceOfEmitInput,
        TrackingNames.ByTypeNameEmitInput,
    ];

    private static readonly string[] ResultsSteps =
    [
        TrackingNames.ByNamespaceResults,
        TrackingNames.ByBaseTypeResults,
        TrackingNames.ByNamespaceOfResults,
        TrackingNames.ByTypeNameResults,
    ];

    private static readonly string[] RulesSteps =
    [
        TrackingNames.ByNamespaceRules,
        TrackingNames.ByBaseTypeRules,
        TrackingNames.ByNamespaceOfRules,
        TrackingNames.ByTypeNameRules,
    ];

    public static TheoryData<string> CallSitePreservingEdits =>
    [
        NonDiEdits.MethodBodyInUnrelatedFile,
        NonDiEdits.NewUnrelatedFile,
        TrailingCommentInConventionsFile,
        TrailingStatementInConventionsFile,
    ];

    public const string TrailingCommentInConventionsFile = "trailing comment in conventions file";
    public const string TrailingStatementInConventionsFile = "trailing statement in conventions file";

    private const string LastCallSite = "ByTypeName((ns, name) => name.EndsWith(\"Repository\"), Lifetime.Scoped);";

    private static Func<Compilation, Compilation> GetEdit(string name) => name switch
    {
        TrailingCommentInConventionsFile => CompilationEdits.InsertAfter(
            IncrementalTestProject.StartupPath, LastCallSite, "\n            // Registrations above are convention based."),
        TrailingStatementInConventionsFile => CompilationEdits.InsertAfter(
            IncrementalTestProject.StartupPath, LastCallSite, "\n            System.Console.WriteLine(\"configured\");"),
        _ => NonDiEdits.Get(name),
    };

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void NonDiEdit_BuilderInputTypePoolAndEmitInputs_AreModified(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        foreach (var name in CompilationBasedSteps)
            AssertAllReasons(IncrementalStepRunReason.Modified, run.EditedReasons<ServicefyConventionsGenerator>(name), name);
    }

    [Theory]
    [MemberData(nameof(CallSitePreservingEdits))]
    public void NonDiEdit_CallSiteResults_AreUnchanged_AndRules_AreCached(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, GetEdit(edit));

        foreach (var name in ResultsSteps)
            Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(name));

        foreach (var name in RulesSteps)
            Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(name));
    }

    [Theory]
    [MemberData(nameof(CallSitePreservingEdits))]
    public void NonDiEdit_CallSiteValues_AreEqualToThePreviousRun(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, GetEdit(edit));

        foreach (var name in ResultsSteps.Concat(RulesSteps))
        {
            var before = run.InitialValues<ServicefyConventionsGenerator>(name);
            var after = run.EditedValues<ServicefyConventionsGenerator>(name);

            Assert.Equal(before.Count, after.Count);
            for (var i = 0; i < before.Count; i++)
            {
                Assert.True(before[i].Equals(after[i]), $"'{name}'[{i}]: {before[i]} != {after[i]}");
                Assert.Equal(before[i].GetHashCode(), after[i].GetHashCode());
            }
        }
    }

    [Theory]
    [MemberData(nameof(CallSitePreservingEdits))]
    public void NonDiEdit_CallSiteDiagnosticsOutputs_AreCached_RegistrationOutputsStillReRun(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, GetEdit(edit));

        var outputs = run.EditedSourceOutputSteps<ServicefyConventionsGenerator>();
        Assert.Equal(9, outputs.Length);

        foreach (var name in CompilationBasedSteps.Except([TrackingNames.ConventionsTypePool]))
            AssertAllReasons(
                IncrementalStepRunReason.Modified,
                run.EditedSourceOutputReasonsFedBy<ServicefyConventionsGenerator>(name),
                $"source output fed by {name}");

        var diagnosticsOutputs = outputs
            .Where(step => step.Inputs.All(input => input.Source.Name is null))
            .ToList();
        Assert.Equal(4, diagnosticsOutputs.Count);
        AssertAllReasons(IncrementalStepRunReason.Cached, IncrementalRun.Reasons(diagnosticsOutputs), "call-site diagnostics outputs");
    }

    [Fact]
    public void CommentAboveCallSites_LocationFreeRulesUnchanged_ByBaseTypeRuleFollowsItsMovedLocation()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources, NonDiEdits.Get(NonDiEdits.CommentInConventionsFile));

        foreach (var (results, rules) in new[]
                 {
                     (TrackingNames.ByNamespaceResults, TrackingNames.ByNamespaceRules),
                     (TrackingNames.ByNamespaceOfResults, TrackingNames.ByNamespaceOfRules),
                     (TrackingNames.ByTypeNameResults, TrackingNames.ByTypeNameRules),
                 })
        {
            Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(results));
            Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(rules));
        }

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeResults));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules));

        var diagnosticsOutputs = run.EditedSourceOutputSteps<ServicefyConventionsGenerator>()
            .Where(step => step.Inputs.All(input => input.Source.Name is null))
            .ToList();
        Assert.Equal(4, diagnosticsOutputs.Count);
        var reasons = IncrementalRun.Reasons(diagnosticsOutputs);
        Assert.Equal(3, reasons.Count(r => r == IncrementalStepRunReason.Cached));
        Assert.Single(reasons, IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void RerunOverSameCompilation_TypePool_IsCached()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool));
        Assert.Same(
            run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool).Single(),
            run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool).Single());
    }

    [Fact]
    public void RerunOverSameCompilation_Builder_IsCached()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsBuilderInput));
        Assert.Equal(
            [IncrementalStepRunReason.Cached],
            run.EditedSourceOutputReasonsFedBy<ServicefyConventionsGenerator>(TrackingNames.ConventionsBuilderInput));
    }

    [Theory]
    [InlineData(TrackingNames.ByNamespaceResults, TrackingNames.ByNamespaceRules, TrackingNames.ByNamespaceEmitInput)]
    [InlineData(TrackingNames.ByBaseTypeResults, TrackingNames.ByBaseTypeRules, TrackingNames.ByBaseTypeEmitInput)]
    [InlineData(TrackingNames.ByNamespaceOfResults, TrackingNames.ByNamespaceOfRules, TrackingNames.ByNamespaceOfEmitInput)]
    [InlineData(TrackingNames.ByTypeNameResults, TrackingNames.ByTypeNameRules, TrackingNames.ByTypeNameEmitInput)]
    public void RerunOverSameCompilation_EveryConvention_SkipsItsEmitter(string results, string rules, string emitInput)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(results));
        Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(rules));
        Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(emitInput));
        Assert.Equal(
            [IncrementalStepRunReason.Cached],
            run.EditedSourceOutputReasonsFedBy<ServicefyConventionsGenerator>(emitInput));
    }

    [Fact]
    public void RerunOverSameCompilation_EverySourceOutput_IsCached()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, CompilationEdits.None);

        AssertAllReasons(
            IncrementalStepRunReason.Cached,
            IncrementalRun.Reasons(run.EditedSourceOutputSteps<ServicefyConventionsGenerator>()),
            "Conventions source outputs");
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

        foreach (var name in new[] { TrackingNames.ByNamespaceOfResults, TrackingNames.ByTypeNameResults })
            Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(name));
        foreach (var name in new[] { TrackingNames.ByNamespaceOfRules, TrackingNames.ByTypeNameRules })
            Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(name));

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
    public void ChangingByBaseTypeLifetime_OnlyByBaseTypeStepsModified_AndRegistrationsChange()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.ReplaceText(
                IncrementalTestProject.StartupPath,
                "ByBaseType<TestAssembly.Handlers.IHandler>(Lifetime.Transient)",
                "ByBaseType<TestAssembly.Handlers.IHandler>(Lifetime.Singleton)"));

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeResults));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules));

        foreach (var name in new[] { TrackingNames.ByNamespaceResults, TrackingNames.ByNamespaceOfResults, TrackingNames.ByTypeNameResults })
            Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(name));
        foreach (var name in new[] { TrackingNames.ByNamespaceRules, TrackingNames.ByNamespaceOfRules, TrackingNames.ByTypeNameRules })
            Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(name));

        const string hint = "ServicefyConventionsBuilder.ByBaseType.g.cs";
        Assert.Contains(
            "Services.AddTransient<global::TestAssembly.Handlers.IHandler, global::TestAssembly.Handlers.CreateOrderHandler>();",
            run.InitialGeneratedSource<ServicefyConventionsGenerator>(hint));
        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>(hint);
        Assert.NotNull(after);
        Assert.Contains("lifetime == Lifetime.Singleton", after);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.Handlers.IHandler, global::TestAssembly.Handlers.CreateOrderHandler>();", after);
        Assert.DoesNotContain("AddTransient<", after);
    }

    [Fact]
    public void ChangingByTypeNamePredicate_ByTypeNameStepsModified_AndRegistrationsFollowTheNewPredicate()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.ReplaceText(
                IncrementalTestProject.StartupPath,
                "name.EndsWith(\"Repository\")",
                "name.EndsWith(\"Handler\")"));

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameResults));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByTypeNameRules));

        foreach (var name in new[] { TrackingNames.ByNamespaceResults, TrackingNames.ByBaseTypeResults, TrackingNames.ByNamespaceOfResults })
            Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(name));

        const string hint = "ServicefyConventionsBuilder.ByTypeName.g.cs";
        var before = run.InitialGeneratedSource<ServicefyConventionsGenerator>(hint);
        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>(hint);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Repositories.IOrderRepository, global::TestAssembly.Repositories.OrderRepository>();", before);
        Assert.DoesNotContain("CreateOrderHandler", before);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Handlers.IHandler, global::TestAssembly.Handlers.CreateOrderHandler>();", after);
        Assert.DoesNotContain("OrderRepository", after);
    }

    [Fact]
    public void AddingByBaseTypeCall_ExistingCallSiteUnchanged_NewEntryReported_AndRegistrationsChange()
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
        Assert.Single(reasons, IncrementalStepRunReason.Unchanged);
        Assert.Single(reasons, IncrementalStepRunReason.New);

        var rules = run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules);
        Assert.Equal(2, rules.Count);
        Assert.Single(rules, IncrementalStepRunReason.Cached);
        Assert.Single(rules, IncrementalStepRunReason.New);

        const string hint = "ServicefyConventionsBuilder.ByBaseType.g.cs";
        Assert.DoesNotContain("global::TestAssembly.Validation.OrderValidator", run.InitialGeneratedSource<ServicefyConventionsGenerator>(hint));
        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>(hint);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.Validation.IValidator, global::TestAssembly.Validation.OrderValidator>();", after);
        Assert.Contains("Services.AddTransient<global::TestAssembly.Handlers.IHandler, global::TestAssembly.Handlers.CreateOrderHandler>();", after);
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

        foreach (var name in new[] { TrackingNames.ByNamespaceResults, TrackingNames.ByBaseTypeResults, TrackingNames.ByNamespaceOfResults })
            Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(name));

        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByTypeName.g.cs"));
        Assert.Null(run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByTypeName.g.cs"));
    }

    [Fact]
    public void AddingTypeMatchedByConvention_UntouchedCallSitesUnchanged_AndRegistrationsIncludeIt()
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

        foreach (var name in ResultsSteps)
            Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(name));
        foreach (var name in RulesSteps)
            Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(name));

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

    [Fact]
    public void AddingImplementationOfByBaseTypeBase_CallSiteUnchanged_AndRegistrationsIncludeIt()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.AddSource("CancelOrderHandler.cs", """
                namespace TestAssembly.Handlers
                {
                    public class CancelOrderHandler : IHandler { }
                }
                """));

        Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeResults));
        Assert.Equal([IncrementalStepRunReason.Cached], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules));

        const string hint = "ServicefyConventionsBuilder.ByBaseType.g.cs";
        Assert.DoesNotContain("CancelOrderHandler", run.InitialGeneratedSource<ServicefyConventionsGenerator>(hint));
        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>(hint);
        Assert.Contains("Services.AddTransient<global::TestAssembly.Handlers.IHandler, global::TestAssembly.Handlers.CancelOrderHandler>();", after);
        Assert.Contains("Services.AddTransient<global::TestAssembly.Handlers.IHandler, global::TestAssembly.Handlers.CreateOrderHandler>();", after);
    }

    [Fact]
    public void EditingBodyOfByBaseTypeBase_CallSiteUnchanged()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.Combine(
                CompilationEdits.ReplaceText(
                    IncrementalTestProject.ServicesPath,
                    "public interface IHandler { }",
                    "public interface IHandler { void Handle(); }"),
                CompilationEdits.ReplaceText(
                    IncrementalTestProject.ServicesPath,
                    "public class CreateOrderHandler : IHandler { }",
                    "public class CreateOrderHandler : IHandler { public void Handle() { } }")));

        foreach (var name in ResultsSteps)
            Assert.Equal([IncrementalStepRunReason.Unchanged], run.EditedReasons<ServicefyConventionsGenerator>(name));

        Assert.Equal(
            run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"),
            run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"));
    }

    [Fact]
    public void RenamingByBaseTypeBase_UntouchedCallSiteIsDetectedAsChanged()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.Combine(
                CompilationEdits.ReplaceText(IncrementalTestProject.ServicesPath, "public interface IHandler { }", "public interface ICommandHandler { }"),
                CompilationEdits.ReplaceText(IncrementalTestProject.ServicesPath, "CreateOrderHandler : IHandler", "CreateOrderHandler : ICommandHandler")));

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeResults));
        Assert.Equal([IncrementalStepRunReason.Removed], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules));

        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"));
        Assert.Null(run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"));
    }

    [Fact]
    public void ChangingByBaseTypeBaseFromInterfaceToClass_UntouchedCallSiteIsDetectedAsChanged()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources,
            CompilationEdits.ReplaceText(IncrementalTestProject.ServicesPath, "public interface IHandler { }", "public abstract class IHandler { }"));

        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeResults));
        Assert.Equal([IncrementalStepRunReason.Modified], run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ByBaseTypeRules));

        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs");
        Assert.NotNull(after);
        Assert.Contains("global::TestAssembly.Handlers.CreateOrderHandler", after);
    }
}
