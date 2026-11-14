using Microsoft.CodeAnalysis;
using Servicefy.Package;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Package.Conventions.Generators.Emit;
using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Incremental.IncrementalStepTrackingTests;
using static Servicefy.Tests.Incremental.ProjectReferenceTestProject;

namespace Servicefy.Tests.Incremental;

public class TypePoolIncrementalTests
{
    private static readonly string[] EmitInputSteps =
    [
        TrackingNames.ByNamespaceEmitInput,
        TrackingNames.ByBaseTypeEmitInput,
        TrackingNames.ByNamespaceOfEmitInput,
        TrackingNames.ByTypeNameEmitInput,
    ];

    [Theory]
    [MemberData(nameof(NonDiEdits.Names), MemberType = typeof(NonDiEdits))]
    public void EveryRun_BuildsOnePool_SharedByAllFourConventionEmitters(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(IncrementalTestProject.Sources, NonDiEdits.Get(edit));

        foreach (var (values, emitInputs, which) in new[]
                 {
                     (run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool),
                      EmitInputSteps.Select(s => run.InitialValues<ServicefyConventionsGenerator>(s)).ToList(), "initial"),
                     (run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool),
                      EmitInputSteps.Select(s => run.EditedValues<ServicefyConventionsGenerator>(s)).ToList(), "edited"),
                 })
        {
            var pool = Assert.IsType<ConventionTypePool>(Assert.Single(values));

            for (var i = 0; i < EmitInputSteps.Length; i++)
            {
                var emitInput = Assert.Single(emitInputs[i]);
                Assert.True(
                    ReferenceEquals(pool, PoolOf(emitInput)),
                    $"{which}: '{EmitInputSteps[i]}' does not receive the run's shared type pool.");
            }
        }

        Assert.NotSame(
            run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool).Single(),
            run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool).Single());
    }

    [Fact]
    public void Pool_IsBuiltForTheRunsCompilation()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            IncrementalTestProject.Sources, NonDiEdits.Get(NonDiEdits.NewUnrelatedFile));

        Assert.Same(run.InitialCompilation, InitialPool(run).Compilation);
        Assert.Same(run.EditedCompilation, EditedPool(run).Compilation);

        Assert.DoesNotContain(InitialPool(run).AllTypes, t => t.Name == "OrderDto");
        Assert.Contains(EditedPool(run).AllTypes, t => t.Name == "OrderDto");
    }

    [Fact]
    public void Pool_HoldsEverySourceTypeAndPublicProjectReferenceTypeExactlyOnce()
    {
        var features = Features(FeaturesV1);
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.None, additionalReferences: [features]);
        var pool = InitialPool(run);

        var types = pool.AllTypes;
        Assert.Equal(types.Count, types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).Count());

        var names = types.Select(t => t.ToDisplayString()).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TestAssembly.Startup", names);
        Assert.Contains("TestAssembly.Local.TimingBillingDecorator", names);
        Assert.Contains("TestAssembly.Utilities.MathUtil", names);

        Assert.Contains("Features.Billing.BillingService", names);
        Assert.Contains("Features.Billing.AuditBillingDecorator", names);
        Assert.Contains("Features.Data.LoggingRepository<T>", names);
        Assert.DoesNotContain("Features.Billing.InternalBillingService", names);

        Assert.DoesNotContain("System.Object", names);
        Assert.DoesNotContain(types, t => t.ContainingAssembly.Name is "System.Runtime" or "System.Private.CoreLib" or "netstandard");

        Assert.Equal([FeaturesAssemblyName], pool.ProjectReferenceAssemblies.Select(a => a.Name));
    }

    private static readonly string[] SourceEditNames =
    [
        "method body in unrelated file",
        "new unrelated file",
        "add local decorate call",
        "edit local decorator",
    ];

    public static TheoryData<string> SourceEdits => new(SourceEditNames);

    private static Func<Compilation, Compilation> GetSourceEdit(string name) => name switch
    {
        "method body in unrelated file" => CompilationEdits.ReplaceText(UtilitiesPath, "return a + b;", "return b + a;"),
        "new unrelated file" => CompilationEdits.AddSource("Dto.cs", "namespace TestAssembly.Dtos { public class OrderDto { } }"),
        "add local decorate call" => CompilationEdits.InsertAfter(
            StartupPath, LoggingDecorateCall, "\n            services.Decorate<IBillingService, TestAssembly.Local.TimingBillingDecorator>();"),
        "edit local decorator" => CompilationEdits.ReplaceText(
            LocalPath, "public TimingBillingDecorator(IBillingService inner) { }", "public TimingBillingDecorator(IBillingService inner) { _ = inner; }"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Theory]
    [MemberData(nameof(SourceEdits))]
    public void SourceEdit_ProjectReferenceIsNotWalkedAgain(string edit)
    {
        var features = Features(FeaturesV1);
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetSourceEdit(edit), additionalReferences: [features]);

        var initialAssembly = Assert.Single(InitialPool(run).ProjectReferenceAssemblies);
        var editedAssembly = Assert.Single(EditedPool(run).ProjectReferenceAssemblies);

        Assert.Same(
            TypeCollector.GetAccessibleTypes(initialAssembly),
            TypeCollector.GetAccessibleTypes(editedAssembly));

        var initialExternal = ExternalTypes(InitialPool(run));
        var editedExternal = ExternalTypes(EditedPool(run));
        Assert.Equal(initialExternal.Count, editedExternal.Count);
        for (var i = 0; i < initialExternal.Count; i++)
            Assert.Same(initialExternal[i], editedExternal[i]);
        Assert.Equal(TypeCollector.GetAccessibleTypes(editedAssembly), editedExternal);
    }

    [Fact]
    public void ManySourceEdits_ProjectReferenceWalkedOnce()
    {
        var features = Features(FeaturesV1);
        var runs = IncrementalGeneratorHelper.RunSequence(
            Sources,
            SourceEditNames.Select(GetSourceEdit),
            additionalReferences: [features]);

        var walks = runs
            .Select(r => TypeCollector.GetAccessibleTypes(Assert.Single(PoolOf(r.Result).ProjectReferenceAssemblies)))
            .ToList();

        Assert.All(walks, w => Assert.Same(walks[0], w));

        for (var i = 0; i < runs.Count; i++)
            GeneratorOutputAssert.EqualToFreshRun(runs[i].Result, runs[i].Compilation, $"run {i}");
    }

    [Fact]
    public void ReplacingProjectReference_NewBuildIsWalked_AndOutputFollowsIt()
    {
        var v1 = Features(FeaturesV1);
        var v2 = Features(FeaturesV2);
        var run = IncrementalGeneratorHelper.RunWithEdit(
            Sources, CompilationEdits.ReplaceReference(v1, v2), additionalReferences: [v1]);

        var initialWalk = TypeCollector.GetAccessibleTypes(Assert.Single(InitialPool(run).ProjectReferenceAssemblies));
        var editedWalk = TypeCollector.GetAccessibleTypes(Assert.Single(EditedPool(run).ProjectReferenceAssemblies));

        Assert.NotSame(initialWalk, editedWalk);
        Assert.DoesNotContain(initialWalk, t => t.Name == "RefundService");
        Assert.Contains(editedWalk, t => t.Name == "RefundService");
        Assert.Contains(EditedPool(run).AllTypes, t => t.Name == "RetryBillingDecorator");

        GeneratorOutputAssert.EqualToFreshRun(run.Edited, run.EditedCompilation, "after reference change");

        Assert.Equal(
            ["Features.Billing.LoggingBillingDecorator", "Features.Billing.AuditBillingDecorator"],
            ByNamespaceChain(run.Initial, BillingService));
        Assert.Equal(
            ["Features.Billing.LoggingBillingDecorator", "Features.Billing.AuditBillingDecorator", "Features.Billing.RetryBillingDecorator"],
            ByNamespaceChain(run.Edited, BillingService));

        var after = ByNamespaceSource(run.Edited);
        Assert.Contains($"Services.AddScoped<{RefundService}, global::Features.Billing.RefundService>();", after);
        Assert.DoesNotContain(", global::Features.Billing.RetryBillingDecorator>", after);
    }

    [Fact]
    public void ReplacingProjectReferenceAndBack_OutputReturnsToTheOriginal()
    {
        var v1 = Features(FeaturesV1);
        var v2 = Features(FeaturesV2);
        var runs = IncrementalGeneratorHelper.RunSequence(
            Sources,
            [CompilationEdits.ReplaceReference(v1, v2), CompilationEdits.ReplaceReference(v2, v1)],
            additionalReferences: [v1]);

        for (var i = 0; i < runs.Count; i++)
            GeneratorOutputAssert.EqualToFreshRun(runs[i].Result, runs[i].Compilation, $"run {i}");

        Assert.Equal(
            GeneratorOutputAssert.AllGeneratedText(runs[0].Result),
            GeneratorOutputAssert.AllGeneratedText(runs[2].Result));
    }

    [Fact]
    public void RemovingProjectReference_RegistrationsOfItsTypesDisappear()
    {
        var features = Features(FeaturesV1);
        var run = IncrementalGeneratorHelper.RunWithEdit(
            Sources, CompilationEdits.RemoveReference(features), additionalReferences: [features]);

        Assert.Empty(EditedPool(run).ProjectReferenceAssemblies);
        Assert.DoesNotContain(EditedPool(run).AllTypes, t => t.ContainingAssembly.Name == FeaturesAssemblyName);

        GeneratorOutputAssert.EqualToFreshRun(run.Edited, run.EditedCompilation, "after reference removal");
        Assert.NotNull(run.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByNamespace.g.cs"));
        Assert.Null(run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByNamespace.g.cs"));
        Assert.Null(run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"));
    }

    [Fact]
    public void AddingProjectReference_ItsTypesAreRegisteredAndDecorated()
    {
        var features = Features(FeaturesV1);
        var withoutReference = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.None);

        var runs = IncrementalGeneratorHelper.RunSequence(Sources, [c => c.AddReferences(features)]);

        Assert.Null(withoutReference.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByNamespace.g.cs"));
        GeneratorOutputAssert.EqualToFreshRun(runs[1].Result, runs[1].Compilation, "after adding the reference");
        Assert.Equal(
            ["Features.Billing.LoggingBillingDecorator", "Features.Billing.AuditBillingDecorator"],
            ByNamespaceChain(runs[1].Result, BillingService));
    }

    public static TheoryData<string, string> ReferencedDecoratorEdits => new()
    {
        { "none", "Features.Billing.LoggingBillingDecorator|Features.Billing.AuditBillingDecorator" },
        { "method body in unrelated file", "Features.Billing.LoggingBillingDecorator|Features.Billing.AuditBillingDecorator" },
        { "add local decorate call", "Features.Billing.LoggingBillingDecorator|TestAssembly.Local.TimingBillingDecorator|Features.Billing.AuditBillingDecorator" },
        { "local decorate call first", "TestAssembly.Local.TimingBillingDecorator|Features.Billing.LoggingBillingDecorator|Features.Billing.AuditBillingDecorator" },
        { "referenced decorate call twice", "Features.Billing.LoggingBillingDecorator|Features.Billing.AuditBillingDecorator" },
    };

    private static Func<Compilation, Compilation> GetReferencedDecoratorEdit(string name) => name switch
    {
        "none" => CompilationEdits.None,
        "local decorate call first" => CompilationEdits.ReplaceText(
            StartupPath, LoggingDecorateCall,
            "services.Decorate<IBillingService, TestAssembly.Local.TimingBillingDecorator>();\n            " + LoggingDecorateCall),
        "remove referenced decorate call" => CompilationEdits.ReplaceText(StartupPath, LoggingDecorateCall, ""),
        "referenced decorate call twice" => CompilationEdits.InsertAfter(StartupPath, LoggingDecorateCall, "\n            " + LoggingDecorateCall.Replace("services.", "services .", StringComparison.Ordinal)),
        _ => GetSourceEdit(name),
    };

    [Theory]
    [MemberData(nameof(ReferencedDecoratorEdits))]
    public void SourceEdit_DecoratorsFromProjectReference_ChainIsExact(string edit, string expectedChain)
    {
        var features = Features(FeaturesV1);
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetReferencedDecoratorEdit(edit), additionalReferences: [features]);

        GeneratorOutputAssert.EqualToFreshRun(run.Edited, run.EditedCompilation, edit);
        Assert.Equal(expectedChain.Split('|'), ByNamespaceChain(run.Edited, BillingService));

        Assert.Equal(
            ["Features.Data.LoggingRepository<global::Features.Data.Customer>"],
            GeneratorOutputAssert.DecoratorChain(
                run.EditedGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs")!,
                CustomerRepository));

        Assert.DoesNotContain(", global::Features.Billing.AuditBillingDecorator>", ByNamespaceSource(run.Edited));
        Assert.DoesNotContain(", global::Features.Billing.LoggingBillingDecorator>", ByNamespaceSource(run.Edited));
    }

    [Fact]
    public void RemovingReferencedDecorateCall_ClassIsNoLongerALayer_AndIsRegisteredByConvention()
    {
        var features = Features(FeaturesV1);
        var run = IncrementalGeneratorHelper.RunWithEdit(
            Sources, GetReferencedDecoratorEdit("remove referenced decorate call"), additionalReferences: [features]);

        GeneratorOutputAssert.EqualToFreshRun(run.Edited, run.EditedCompilation, "remove referenced decorate call");

        const string loggingKey = "\"Features.Billing.LoggingBillingDecorator\"";
        const string loggingAsImplementation = $"<{BillingService}, global::Features.Billing.LoggingBillingDecorator>";

        Assert.Contains(loggingKey, ByNamespaceSource(run.Initial));
        Assert.DoesNotContain(loggingAsImplementation, ByNamespaceSource(run.Initial));
        Assert.DoesNotContain(loggingKey, ByNamespaceSource(run.Edited));
        Assert.Contains(loggingAsImplementation, ByNamespaceSource(run.Edited));
    }

    [Fact]
    public void DuplicateReferencedDecorator_IsReportedAtTheSecondCall()
    {
        var features = Features(FeaturesV1);
        var run = IncrementalGeneratorHelper.RunWithEdit(
            Sources, GetReferencedDecoratorEdit("referenced decorate call twice"), additionalReferences: [features]);

        var diagnostic = Assert.Single(run.Edited.Diagnostics);
        Assert.Equal("SVCFY015", diagnostic.Id);
        Assert.Equal(StartupPath, diagnostic.Location.GetLineSpan().Path);
        Assert.Equal("services .Decorate<IBillingService, LoggingBillingDecorator>()", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
        GeneratorOutputAssert.AssertLocatedInCompilation(run.Edited.Diagnostics, run.EditedCompilation, "duplicate");
    }

    private const string SharedPoolTypes = """
        namespace TestAssembly.Data
        {
            public interface IRepository<T> { }
            public class Order { }
            public class OrderRepository : IRepository<Order> { }

            public class LoggingRepository<T> : IRepository<T>
            {
                public LoggingRepository(IRepository<T> inner) { }
            }
        }
        """;

    private static string SharedPoolStartup(bool withByBaseType) => $$"""
        using TestAssembly.Data;

        namespace TestAssembly
        {
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddServicefyConventions().Decorate(typeof(IRepository<>), typeof(LoggingRepository<>));
                    {{(withByBaseType ? "services.AddServicefyConventions().ByBaseType(typeof(IRepository<>), Lifetime.Scoped);" : "")}}
                    services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith("Repository"), Lifetime.Scoped);
                    services.AddServicefyConventions().ByNamespaceOf<Order>(ns => true, Lifetime.Scoped);
                }
            }
        }
        """;

    [Fact]
    public void OpenGenericDecoratorExpansionInByBaseType_DoesNotLeakIntoOtherEmitters()
    {
        var with = IncrementalGeneratorHelper.RunWithEdit(
            [("Types.cs", SharedPoolTypes), ("Startup.cs", SharedPoolStartup(withByBaseType: true))], CompilationEdits.None);
        var without = IncrementalGeneratorHelper.RunWithEdit(
            [("Types.cs", SharedPoolTypes), ("Startup.cs", SharedPoolStartup(withByBaseType: false))], CompilationEdits.None);

        Assert.NotNull(with.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"));
        Assert.Contains(
            "new global::TestAssembly.Data.LoggingRepository<global::TestAssembly.Data.Order>(",
            with.InitialGeneratedSource<ServicefyConventionsGenerator>("ServicefyConventionsBuilder.ByBaseType.g.cs"));

        foreach (var hint in new[] { "ServicefyConventionsBuilder.ByTypeName.g.cs", "ServicefyConventionsBuilder.ByNamespaceOf.g.cs" })
        {
            Assert.NotNull(without.InitialGeneratedSource<ServicefyConventionsGenerator>(hint));
            Assert.Equal(
                without.InitialGeneratedSource<ServicefyConventionsGenerator>(hint),
                with.InitialGeneratedSource<ServicefyConventionsGenerator>(hint));
        }
    }

    private static ConventionTypePool InitialPool(IncrementalRun run) =>
        Assert.IsType<ConventionTypePool>(run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool).Single());

    private static ConventionTypePool EditedPool(IncrementalRun run) =>
        Assert.IsType<ConventionTypePool>(run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsTypePool).Single());

    private static ConventionTypePool PoolOf(GeneratorDriverRunResult run)
    {
        var result = run.Results.Single(r => r.Generator.GetGeneratorType() == typeof(ServicefyConventionsGenerator));
        return Assert.IsType<ConventionTypePool>(
            IncrementalRun.Values(result.TrackedSteps[TrackingNames.ConventionsTypePool]).Single());
    }

    private static object? PoolOf(object emitInputValue) =>
        emitInputValue.GetType().GetField("Item1")?.GetValue(emitInputValue);

    private static List<INamedTypeSymbol> ExternalTypes(ConventionTypePool pool) =>
        pool.AllTypes.Where(t => t.ContainingAssembly.Name == FeaturesAssemblyName).ToList();

    private static string ByNamespaceSource(GeneratorDriverRunResult run)
    {
        var source = run.Results
            .Single(r => r.Generator.GetGeneratorType() == typeof(ServicefyConventionsGenerator))
            .GeneratedSources.SingleOrDefault(s => s.HintName == "ServicefyConventionsBuilder.ByNamespace.g.cs")
            .SourceText?.ToString();
        Assert.True(source is not null, "ServicefyConventionsBuilder.ByNamespace.g.cs was not generated.");
        return source;
    }

    private static IReadOnlyList<string> ByNamespaceChain(GeneratorDriverRunResult run, string service) =>
        GeneratorOutputAssert.DecoratorChain(ByNamespaceSource(run), service);
}
