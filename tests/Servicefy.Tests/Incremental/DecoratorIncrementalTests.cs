using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Servicefy.Package;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Incremental.DecoratorTestProject;
using static Servicefy.Tests.Incremental.IncrementalStepTrackingTests;

namespace Servicefy.Tests.Incremental;

public class DecoratorIncrementalTests
{
    private const string ExtensionsHint = "ServicefyExtensions.TestAssembly.g.cs";
    private const string ByNamespaceHint = "ServicefyConventionsBuilder.ByNamespace.g.cs";
    private const string ByBaseTypeHint = "ServicefyConventionsBuilder.ByBaseType.g.cs";

    public const string MethodBodyInUnrelatedFile = "method body in unrelated file";
    public const string NewUnrelatedFile = "new unrelated file";
    public const string CommentAboveDecorateCalls = "comment above decorate calls";
    public const string TrailingStatementInStartup = "trailing statement in startup";
    public const string AddFluentDecorateCall = "add fluent decorate call";
    public const string RemoveFluentDecorateCall = "remove fluent decorate call";
    public const string SwapFluentDecorateCalls = "swap fluent decorate calls";
    public const string ChangeDecorateTypeArgument = "change decorate type argument";
    public const string MoveDecorateCallToEarlierFile = "move decorate call to earlier file";
    public const string MoveDecorateCallToLaterFile = "move decorate call to later file";
    public const string MoveDecorateCallWithinFile = "move decorate call within file";
    public const string AddOpenGenericDecorateCall = "add open-generic decorate call";
    public const string RemoveOpenGenericDecorateCall = "remove open-generic decorate call";
    public const string AddDecoratorForAttribute = "add [DecoratorFor] attribute";
    public const string RemoveDecoratorForAttribute = "remove [DecoratorFor] attribute";
    public const string RemoveDecoratorAttribute = "remove [Decorator] attribute";
    public const string DecoratorLosesInnerConstructor = "decorator loses inner constructor";
    public const string DecoratorStopsImplementingService = "decorator stops implementing service";
    public const string DuplicateDecoratorViaFluentCall = "duplicate decorator via fluent call";
    public const string RenameDecoratorUsedByDecorateCall = "rename decorator used by decorate call";
    public const string DeleteStartupFile = "delete startup file";

    public static TheoryData<string> AllEdits =>
    [
        MethodBodyInUnrelatedFile,
        NewUnrelatedFile,
        CommentAboveDecorateCalls,
        TrailingStatementInStartup,
        AddFluentDecorateCall,
        RemoveFluentDecorateCall,
        SwapFluentDecorateCalls,
        ChangeDecorateTypeArgument,
        MoveDecorateCallToEarlierFile,
        MoveDecorateCallToLaterFile,
        MoveDecorateCallWithinFile,
        AddOpenGenericDecorateCall,
        RemoveOpenGenericDecorateCall,
        AddDecoratorForAttribute,
        RemoveDecoratorForAttribute,
        RemoveDecoratorAttribute,
        DecoratorLosesInnerConstructor,
        DecoratorStopsImplementingService,
        DuplicateDecoratorViaFluentCall,
        RenameDecoratorUsedByDecorateCall,
        DeleteStartupFile,
    ];

    private const string LoggingCall = "services.Decorate<IOrderService, LoggingOrderDecorator>()";
    private const string CachingCall = ".Decorate<IOrderService, CachingOrderDecorator>()";

    private static string CompositionFile(string className) => $$"""
        using TestAssembly.Orders;

        namespace TestAssembly
        {
            public static class {{className}}
            {
                public static void Configure(IServiceCollection services)
                {
                    services.Decorate<IOrderService, CachingOrderDecorator>();
                }
            }
        }
        """;

    internal static Func<Compilation, Compilation> GetEdit(string name) => name switch
    {
        MethodBodyInUnrelatedFile => CompilationEdits.ReplaceText(UtilitiesPath, "return a + b;", "return b + a;"),
        NewUnrelatedFile => CompilationEdits.AddSource(
            "Dto.cs", "namespace TestAssembly.Dtos { public class OrderDto { public int Id { get; set; } } }"),
        CommentAboveDecorateCalls => CompilationEdits.ReplaceText(
            StartupPath, LoggingCall, "// Decorators, outermost first.\n            " + LoggingCall),
        TrailingStatementInStartup => CompilationEdits.InsertAfter(
            StartupPath, OpenGenericDecorate, "\n            System.Console.WriteLine(\"configured\");"),
        AddFluentDecorateCall => CompilationEdits.InsertAfter(
            StartupPath, OrderChain, "\n            services.Decorate<IOrderService, RetryOrderDecorator>();"),
        RemoveFluentDecorateCall => CompilationEdits.ReplaceText(StartupPath, OrderChain, LoggingCall + ";"),
        SwapFluentDecorateCalls => CompilationEdits.ReplaceText(
            StartupPath,
            OrderChain,
            "services.Decorate<IOrderService, CachingOrderDecorator>()\n                .Decorate<IOrderService, LoggingOrderDecorator>();"),
        ChangeDecorateTypeArgument => CompilationEdits.ReplaceText(
            StartupPath, CachingCall, ".Decorate<IOrderService, RetryOrderDecorator>()"),
        MoveDecorateCallToEarlierFile => CompilationEdits.Combine(
            CompilationEdits.ReplaceText(StartupPath, OrderChain, LoggingCall + ";"),
            CompilationEdits.AddSource("Composition.cs", CompositionFile("Composition"))),
        MoveDecorateCallToLaterFile => CompilationEdits.Combine(
            CompilationEdits.ReplaceText(StartupPath, OrderChain, LoggingCall + ";"),
            CompilationEdits.AddSource("ZComposition.cs", CompositionFile("ZComposition"))),
        MoveDecorateCallWithinFile => CompilationEdits.Combine(
            CompilationEdits.ReplaceText(StartupPath, PaymentDecorateCall + "\n            ", ""),
            CompilationEdits.InsertAfter(StartupPath, ByNamespaceCall, "\n            " + PaymentDecorateCall)),
        AddOpenGenericDecorateCall => CompilationEdits.ReplaceText(
            StartupPath,
            OpenGenericDecorate,
            ".Decorate(typeof(IRepository<>), typeof(LoggingRepository<>))\n                .Decorate(typeof(IRepository<>), typeof(CachingRepository<>));"),
        RemoveOpenGenericDecorateCall => CompilationEdits.ReplaceText(StartupPath, OpenGenericDecorate, ";"),
        AddDecoratorForAttribute => CompilationEdits.ReplaceText(
            DecoratorsPath, "public class RetryOrderDecorator", "[DecoratorFor<IOrderService>]\n    public class RetryOrderDecorator"),
        RemoveDecoratorForAttribute => CompilationEdits.ReplaceText(DecoratorsPath, "[DecoratorFor<IOrderService>]", ""),
        RemoveDecoratorAttribute => CompilationEdits.ReplaceText(DecoratorsPath, "[Decorator]", ""),
        DecoratorLosesInnerConstructor => CompilationEdits.ReplaceText(
            DecoratorsPath, "public CachingOrderDecorator(IOrderService inner) { }", "public CachingOrderDecorator() { }"),
        DecoratorStopsImplementingService => CompilationEdits.ReplaceText(
            DecoratorsPath, "public class CachingOrderDecorator : IOrderService", "public class CachingOrderDecorator"),
        DuplicateDecoratorViaFluentCall => CompilationEdits.InsertAfter(
            StartupPath, OrderChain, "\n            services.Decorate<IOrderService, AuditOrderDecorator>();"),
        RenameDecoratorUsedByDecorateCall => CompilationEdits.Combine(
            CompilationEdits.ReplaceText(DecoratorsPath, "public class CachingOrderDecorator", "public class CacheOrderDecorator"),
            CompilationEdits.ReplaceText(DecoratorsPath, "public CachingOrderDecorator(", "public CacheOrderDecorator(")),
        DeleteStartupFile => CompilationEdits.RemoveSource(StartupPath),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Fact]
    public void InitialRun_EqualsFreshRun_AndHasExpectedChains()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.None);

        Assert.Empty(run.Initial.Diagnostics);
        GeneratorOutputAssert.EqualToFreshRun(run.Initial, run.InitialCompilation, "initial run");

        Assert.Equal(
            ["TestAssembly.Orders.LoggingOrderDecorator", "TestAssembly.Orders.CachingOrderDecorator", "TestAssembly.Orders.AuditOrderDecorator"],
            Chain<ServicefyByConfigurationGenerator>(run.Initial, ExtensionsHint, OrderService));
        Assert.Equal(
            ["TestAssembly.Payments.LoggingPaymentDecorator", "TestAssembly.Payments.MetricsPaymentDecorator"],
            Chain<ServicefyConventionsGenerator>(run.Initial, ByNamespaceHint, PaymentService));
        Assert.Equal(
            ["TestAssembly.Data.LoggingRepository<global::TestAssembly.Data.Order>"],
            Chain<ServicefyConventionsGenerator>(run.Initial, ByBaseTypeHint, OrderRepository));
        Assert.Equal(
            ["TestAssembly.Data.LoggingRepository<global::TestAssembly.Data.Invoice>"],
            Chain<ServicefyConventionsGenerator>(run.Initial, ByBaseTypeHint, InvoiceRepository));
    }

    [Theory]
    [MemberData(nameof(AllEdits))]
    public void Edit_IncrementalOutputAndDiagnostics_EqualFreshRun(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(edit));

        GeneratorOutputAssert.EqualToFreshRun(run.Edited, run.EditedCompilation, edit);
    }

    [Theory]
    [MemberData(nameof(AllEdits))]
    public void EditThenUndo_OutputReturnsToTheOriginal(string edit)
    {
        var runs = IncrementalGeneratorHelper.RunSequence(Sources, [GetEdit(edit), RestoreOriginalSources]);

        GeneratorOutputAssert.EqualToFreshRun(runs[2].Result, runs[2].Compilation, $"undo of {edit}");

        foreach (var generator in runs[0].Result.Results)
        {
            var type = generator.Generator.GetGeneratorType();
            Assert.Equal(
                GeneratorOutputAssert.Sources(generator),
                GeneratorOutputAssert.Sources(runs[2].Result.Results.Single(r => r.Generator.GetGeneratorType() == type)));
        }

        Assert.Empty(runs[2].Result.Diagnostics);
    }

    [Fact]
    public void ManyEditsInARow_EveryRunEqualsFreshRun()
    {
        var edits = new[]
        {
            AddFluentDecorateCall, MethodBodyInUnrelatedFile, CommentAboveDecorateCalls, SwapFluentDecorateCalls,
            AddOpenGenericDecorateCall, DecoratorLosesInnerConstructor, AddDecoratorForAttribute, RemoveDecoratorAttribute,
        };

        var runs = IncrementalGeneratorHelper.RunSequence(Sources, edits.Select(GetEdit));

        for (var i = 0; i < runs.Count; i++)
            GeneratorOutputAssert.EqualToFreshRun(runs[i].Result, runs[i].Compilation, i == 0 ? "initial" : $"after {edits[i - 1]} (step {i})");
    }

    public static TheoryData<string, string, string, string> ExpectedChains => new()
    {
        { MethodBodyInUnrelatedFile, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.CachingOrderDecorator|Orders.AuditOrderDecorator" },
        { CommentAboveDecorateCalls, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.CachingOrderDecorator|Orders.AuditOrderDecorator" },
        { AddFluentDecorateCall, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.CachingOrderDecorator|Orders.RetryOrderDecorator|Orders.AuditOrderDecorator" },
        { RemoveFluentDecorateCall, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.AuditOrderDecorator" },
        { SwapFluentDecorateCalls, ExtensionsHint, OrderService, "Orders.CachingOrderDecorator|Orders.LoggingOrderDecorator|Orders.AuditOrderDecorator" },
        { ChangeDecorateTypeArgument, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.RetryOrderDecorator|Orders.AuditOrderDecorator" },
        { MoveDecorateCallToEarlierFile, ExtensionsHint, OrderService, "Orders.CachingOrderDecorator|Orders.LoggingOrderDecorator|Orders.AuditOrderDecorator" },
        { MoveDecorateCallToLaterFile, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.CachingOrderDecorator|Orders.AuditOrderDecorator" },
        { AddDecoratorForAttribute, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.CachingOrderDecorator|Orders.AuditOrderDecorator|Orders.RetryOrderDecorator" },
        { RemoveDecoratorForAttribute, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.CachingOrderDecorator" },
        { DuplicateDecoratorViaFluentCall, ExtensionsHint, OrderService, "Orders.LoggingOrderDecorator|Orders.CachingOrderDecorator|Orders.AuditOrderDecorator" },
        { MoveDecorateCallWithinFile, ByNamespaceHint, PaymentService, "Payments.LoggingPaymentDecorator|Payments.MetricsPaymentDecorator" },
        { AddOpenGenericDecorateCall, ByBaseTypeHint, OrderRepository, "Data.LoggingRepository<global::TestAssembly.Data.Order>|Data.CachingRepository<global::TestAssembly.Data.Order>" },
        { AddOpenGenericDecorateCall, ByBaseTypeHint, InvoiceRepository, "Data.LoggingRepository<global::TestAssembly.Data.Invoice>|Data.CachingRepository<global::TestAssembly.Data.Invoice>" },
    };

    [Theory]
    [MemberData(nameof(ExpectedChains))]
    public void Edit_DecoratorChainFollowsTheEdit(string edit, string hint, string service, string expectedChain)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(edit));

        var expected = expectedChain.Split('|').Select(d => "TestAssembly." + d).ToArray();
        var actual = hint == ExtensionsHint
            ? Chain<ServicefyByConfigurationGenerator>(run.Edited, hint, service)
            : Chain<ServicefyConventionsGenerator>(run.Edited, hint, service);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemovingOpenGenericDecorateCall_RepositoriesAreNoLongerDecorated()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(RemoveOpenGenericDecorateCall));

        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>(ByBaseTypeHint);
        Assert.NotNull(after);
        Assert.DoesNotContain("\"__BASE__\"", after);
        Assert.Contains($"Services.AddScoped<{OrderRepository}, global::TestAssembly.Data.OrderRepository>();", after);
        Assert.Contains($"Services.AddScoped<{InvoiceRepository}, global::TestAssembly.Data.InvoiceRepository>();", after);
    }

    [Fact]
    public void AddingOpenGenericDecorateCall_NewDecoratorIsNoLongerRegisteredAsPlainImplementation()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(AddOpenGenericDecorateCall));

        Assert.Contains(
            "typeof(global::TestAssembly.Data.CachingRepository<>)",
            run.InitialGeneratedSource<ServicefyConventionsGenerator>(ByBaseTypeHint));

        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>(ByBaseTypeHint);
        Assert.NotNull(after);
        Assert.DoesNotContain("typeof(global::TestAssembly.Data.CachingRepository<>)", after);
    }

    [Fact]
    public void RemovingDecoratorAttribute_ClassIsNoLongerALayer_AndIsRegisteredByConvention()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(RemoveDecoratorAttribute));

        const string metricsKey = "\"TestAssembly.Payments.MetricsPaymentDecorator\"";
        const string metricsAsImplementation = $"<{PaymentService}, global::TestAssembly.Payments.MetricsPaymentDecorator>";

        var before = run.InitialGeneratedSource<ServicefyConventionsGenerator>(ByNamespaceHint);
        Assert.Contains(metricsKey, before);
        Assert.DoesNotContain(metricsAsImplementation, before);

        var after = run.EditedGeneratedSource<ServicefyConventionsGenerator>(ByNamespaceHint);
        Assert.NotNull(after);
        Assert.DoesNotContain(metricsKey, after);
        Assert.Contains(metricsAsImplementation, after);
    }

    [Fact]
    public void DeletingStartupFile_RemovesEveryFluentDecorator()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(DeleteStartupFile));

        Assert.Equal(
            ["TestAssembly.Orders.AuditOrderDecorator"],
            Chain<ServicefyByConfigurationGenerator>(run.Edited, ExtensionsHint, OrderService));
        Assert.Null(run.EditedGeneratedSource<ServicefyConventionsGenerator>(ByNamespaceHint));
        Assert.Null(run.EditedGeneratedSource<ServicefyConventionsGenerator>(ByBaseTypeHint));
    }

    [Theory]
    [InlineData(DecoratorLosesInnerConstructor, "SVCFY007")]
    [InlineData(DecoratorStopsImplementingService, "SVCFY013")]
    public void DecoratorBrokenInAnotherFile_DiagnosticPointsAtTheUntouchedDecorateCall(string edit, string id)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(edit));

        Assert.Empty(run.Initial.Diagnostics);

        var diagnostic = Assert.Single(run.Edited.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Contains("CachingOrderDecorator", diagnostic.GetMessage());
        AssertAt(diagnostic, Startup, StartupPath, LoggingCall);
        GeneratorOutputAssert.AssertLocatedInCompilation(run.Edited.Diagnostics, run.EditedCompilation, edit);

        var extensions = run.EditedGeneratedSource<ServicefyByConfigurationGenerator>(ExtensionsHint);
        Assert.NotNull(extensions);
        Assert.DoesNotContain("\"__BASE__\"", extensions);
        Assert.Contains("global::TestAssembly.Orders.OrderService", extensions);
    }

    [Fact]
    public void DuplicateDecorator_Svcfy015PointsAtTheAttribute()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(DuplicateDecoratorViaFluentCall));

        var diagnostic = Assert.Single(run.Edited.Diagnostics);
        Assert.Equal("SVCFY015", diagnostic.Id);
        Assert.Contains("AuditOrderDecorator", diagnostic.GetMessage());
        AssertAt(diagnostic, Decorators, DecoratorsPath, "DecoratorFor<IOrderService>");
    }

    [Fact]
    public void LinesInsertedAboveDecorateCalls_DiagnosticMovesWithItsCallSite()
    {
        const string header = "// Composition root\n// Decorators are wired at build time.\n";
        var runs = IncrementalGeneratorHelper.RunSequence(
            Sources,
            [GetEdit(DecoratorLosesInnerConstructor), CompilationEdits.ReplaceSource(StartupPath, header + Startup)]);

        var before = Assert.Single(runs[1].Result.Diagnostics);
        var after = Assert.Single(runs[2].Result.Diagnostics);
        Assert.Equal("SVCFY007", after.Id);

        AssertAt(before, Startup, StartupPath, LoggingCall);
        AssertAt(after, header + Startup, StartupPath, LoggingCall);
        Assert.Equal(before.Location.GetLineSpan().StartLinePosition.Line + 2, after.Location.GetLineSpan().StartLinePosition.Line);

        GeneratorOutputAssert.AssertLocatedInCompilation(runs[2].Result.Diagnostics, runs[2].Compilation, "after header");
        GeneratorOutputAssert.EqualToFreshRun(runs[2].Result, runs[2].Compilation, "after header");
    }

    [Fact]
    public void FixingBrokenDecorator_DiagnosticDisappears_AndChainIsBack()
    {
        var runs = IncrementalGeneratorHelper.RunSequence(
            Sources,
            [GetEdit(DecoratorLosesInnerConstructor), RestoreOriginalSources]);

        Assert.Single(runs[1].Result.Diagnostics);
        Assert.Empty(runs[2].Result.Diagnostics);
        Assert.Equal(
            ["TestAssembly.Orders.LoggingOrderDecorator", "TestAssembly.Orders.CachingOrderDecorator", "TestAssembly.Orders.AuditOrderDecorator"],
            Chain<ServicefyByConfigurationGenerator>(runs[2].Result, ExtensionsHint, OrderService));
    }

    public static TheoryData<string> EditsOutsideDecorateCallFiles =>
    [
        MethodBodyInUnrelatedFile,
        NewUnrelatedFile,
        AddDecoratorForAttribute,
        DecoratorLosesInnerConstructor,
    ];

    [Theory]
    [MemberData(nameof(EditsOutsideDecorateCallFiles))]
    public void EditOutsideDecorateCallFiles_DecorateCallSteps_AreCachedWithEqualValues(string edit)
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(edit));

        AssertAllReasons(IncrementalStepRunReason.Cached, run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls), TrackingNames.ConventionsDecorateCalls);
        AssertAllReasons(IncrementalStepRunReason.Cached, run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls), TrackingNames.ByConfigurationDecorateCalls);
        Assert.Equal(4, run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls).Count);
        Assert.Equal(4, run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls).Count);

        Assert.Equal(
            run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls),
            run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls));
        Assert.Equal(
            run.InitialValues<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls),
            run.EditedValues<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls));
    }

    [Fact]
    public void InitialRun_DecorateCallSteps_AreNew_OnePerCallSite()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.None);

        foreach (var reasons in new[]
                 {
                     run.InitialReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls),
                     run.InitialReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls),
                 })
        {
            Assert.Equal(4, reasons.Count);
            Assert.All(reasons, r => Assert.Equal(IncrementalStepRunReason.New, r));
        }
    }

    [Fact]
    public void RerunOverSameCompilation_DecorateCallSteps_AreCached()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, CompilationEdits.None);

        AssertAllReasons(IncrementalStepRunReason.Cached, run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls), TrackingNames.ConventionsDecorateCalls);
        AssertAllReasons(IncrementalStepRunReason.Cached, run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls), TrackingNames.ByConfigurationDecorateCalls);
    }

    [Fact]
    public void AddingDecorateCall_DecorateCallSteps_ReportOneNewEntry()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(AddFluentDecorateCall));

        foreach (var reasons in new[]
                 {
                     run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls),
                     run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls),
                 })
        {
            Assert.Equal(5, reasons.Count);
            Assert.Single(reasons, IncrementalStepRunReason.New);
        }
    }

    [Fact]
    public void RemovingDecorateCall_DecorateCallSteps_ReportOneRemovedEntry()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(RemoveFluentDecorateCall));

        foreach (var reasons in new[]
                 {
                     run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls),
                     run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls),
                 })
            Assert.Single(reasons, IncrementalStepRunReason.Removed);
    }

    [Fact]
    public void RenamingDecoratorElsewhere_UntouchedDecorateCallIsDetectedAsChanged()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(RenameDecoratorUsedByDecorateCall));

        foreach (var (reasons, before, after) in new[]
                 {
                     (run.EditedReasons<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls),
                      run.InitialValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls),
                      run.EditedValues<ServicefyConventionsGenerator>(TrackingNames.ConventionsDecorateCalls)),
                     (run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls),
                      run.InitialValues<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls),
                      run.EditedValues<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls)),
                 })
        {
            Assert.Equal(4, reasons.Count);
            Assert.Single(reasons, IncrementalStepRunReason.Modified);
            Assert.Equal(3, reasons.Count(r => r == IncrementalStepRunReason.Cached));
            Assert.Equal(3, after.Count(before.Contains));
        }
    }

    [Fact]
    public void RenamingDecoratorElsewhere_UnresolvedDecoratorFailsValidationAtTheChain()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(Sources, GetEdit(RenameDecoratorUsedByDecorateCall));

        GeneratorOutputAssert.EqualToFreshRun(run.Edited, run.EditedCompilation, RenameDecoratorUsedByDecorateCall);

        var diagnostic = Assert.Single(run.Edited.Diagnostics);
        Assert.Equal("SVCFY013", diagnostic.Id);
        Assert.Contains("CachingOrderDecorator", diagnostic.GetMessage());
        AssertAt(diagnostic, Startup, StartupPath, LoggingCall);
        GeneratorOutputAssert.AssertLocatedInCompilation(run.Edited.Diagnostics, run.EditedCompilation, RenameDecoratorUsedByDecorateCall);

        var extensions = run.EditedGeneratedSource<ServicefyByConfigurationGenerator>(ExtensionsHint);
        Assert.NotNull(extensions);
        Assert.DoesNotContain("\"__BASE__\"", extensions);
        Assert.Contains("global::TestAssembly.Orders.OrderService", extensions);

        var runs = IncrementalGeneratorHelper.RunSequence(Sources, [GetEdit(RenameDecoratorUsedByDecorateCall), RestoreOriginalSources]);
        Assert.Empty(runs[2].Result.Diagnostics);
        Assert.Equal(
            ["TestAssembly.Orders.LoggingOrderDecorator", "TestAssembly.Orders.CachingOrderDecorator", "TestAssembly.Orders.AuditOrderDecorator"],
            Chain<ServicefyByConfigurationGenerator>(runs[2].Result, ExtensionsHint, OrderService));
    }

    [Fact]
    public void TypeofDecorateCallsThatAreNotOpenGeneric_AreIgnored()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            Sources,
            CompilationEdits.InsertAfter(
                StartupPath,
                OrderChain,
                "\n            services.AddServicefyConventions().Decorate(typeof(IOrderService), typeof(LoggingOrderDecorator));" +
                "\n            services.AddServicefyConventions().Decorate(typeof(IRepository<Order>), typeof(LoggingRepository<Order>));"));

        GeneratorOutputAssert.EqualToFreshRun(run.Edited, run.EditedCompilation, "non-open-generic typeof decorate calls");
        Assert.Empty(run.Edited.Diagnostics);
        Assert.Equal(
            ["TestAssembly.Orders.LoggingOrderDecorator", "TestAssembly.Orders.CachingOrderDecorator", "TestAssembly.Orders.AuditOrderDecorator"],
            Chain<ServicefyByConfigurationGenerator>(run.Edited, ExtensionsHint, OrderService));
        Assert.Equal(
            IncrementalRun.GeneratedSources(run.InitialResult<ServicefyConventionsGenerator>()),
            IncrementalRun.GeneratedSources(run.EditedResult<ServicefyConventionsGenerator>()));
    }

    [Fact]
    public void AddingDecorateCallWithUnknownDecorator_ReportsSvcfy013_AndChainFallsBack()
    {
        var run = IncrementalGeneratorHelper.RunWithEdit(
            Sources,
            CompilationEdits.InsertAfter(StartupPath, OrderChain, "\n            services.Decorate<IOrderService, MissingDecorator>();"));

        GeneratorOutputAssert.EqualToFreshRun(run.Edited, run.EditedCompilation, "unknown decorator");

        var diagnostic = Assert.Single(run.Edited.Diagnostics);
        Assert.Equal("SVCFY013", diagnostic.Id);
        Assert.Contains("MissingDecorator", diagnostic.GetMessage());
        AssertAt(diagnostic, Startup, StartupPath, LoggingCall);

        var extensions = run.EditedGeneratedSource<ServicefyByConfigurationGenerator>(ExtensionsHint);
        Assert.NotNull(extensions);
        Assert.DoesNotContain("\"__BASE__\"", extensions);

        Assert.Single(run.EditedReasons<ServicefyByConfigurationGenerator>(TrackingNames.ByConfigurationDecorateCalls), IncrementalStepRunReason.New);
    }

    [Fact]
    public void DecorateCallWithUnknownService_DecoratorStaysExcludedFromConventions()
    {
        var typo = CompilationEdits.ReplaceText(StartupPath, PaymentDecorateCall, "services.Decorate<IPaymentServic, LoggingPaymentDecorator>();");
        var runs = IncrementalGeneratorHelper.RunSequence(Sources, [typo, RestoreOriginalSources]);

        const string loggingAsImplementation = $"<{PaymentService}, global::TestAssembly.Payments.LoggingPaymentDecorator>";

        for (var i = 0; i < runs.Count; i++)
        {
            GeneratorOutputAssert.EqualToFreshRun(runs[i].Result, runs[i].Compilation, $"run {i}");
            Assert.DoesNotContain(loggingAsImplementation, ByNamespaceSource(runs[i].Result));
        }

        Assert.Equal(["TestAssembly.Payments.MetricsPaymentDecorator"], Chain<ServicefyConventionsGenerator>(runs[1].Result, ByNamespaceHint, PaymentService));
        Assert.Equal(
            ["TestAssembly.Payments.LoggingPaymentDecorator", "TestAssembly.Payments.MetricsPaymentDecorator"],
            Chain<ServicefyConventionsGenerator>(runs[2].Result, ByNamespaceHint, PaymentService));
    }

    private static Compilation RestoreOriginalSources(Compilation compilation)
    {
        var original = Sources.ToDictionary(s => s.Path, s => s.Source, StringComparer.Ordinal);
        var parseOptions = compilation.SyntaxTrees.First().Options;

        foreach (var tree in compilation.SyntaxTrees.ToList())
        {
            if (!original.TryGetValue(tree.FilePath, out var text))
                compilation = compilation.RemoveSyntaxTrees(tree);
            else if (tree.GetText().ToString() != text)
                compilation = compilation.ReplaceSyntaxTree(tree, tree.WithChangedText(SourceText.From(text)));
        }

        foreach (var (path, text) in Sources)
            if (compilation.SyntaxTrees.All(t => t.FilePath != path))
                compilation = compilation.AddSyntaxTrees(
                    Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text, (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)parseOptions, path: path));

        return compilation;
    }

    private static string ByNamespaceSource(GeneratorDriverRunResult run)
    {
        var result = run.Results.Single(r => r.Generator.GetGeneratorType() == typeof(ServicefyConventionsGenerator));
        var source = result.GeneratedSources.SingleOrDefault(s => s.HintName == ByNamespaceHint).SourceText?.ToString();
        Assert.True(source is not null, $"'{ByNamespaceHint}' was not generated.");
        return source;
    }

    private static IReadOnlyList<string> Chain<TGenerator>(GeneratorDriverRunResult run, string hint, string service)
        where TGenerator : IIncrementalGenerator
    {
        var result = run.Results.Single(r => r.Generator.GetGeneratorType() == typeof(TGenerator));
        var source = result.GeneratedSources.SingleOrDefault(s => s.HintName == hint).SourceText?.ToString();
        Assert.True(source is not null, $"'{hint}' was not generated.");
        return GeneratorOutputAssert.DecoratorChain(source, service);
    }

    private static void AssertAt(Diagnostic diagnostic, string source, string path, string anchor)
    {
        var index = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(index >= 0 && source.IndexOf(anchor, index + 1, StringComparison.Ordinal) < 0, $"Anchor '{anchor}' must occur exactly once.");

        var expectedSpan = new TextSpan(index, anchor.Length);
        Assert.Equal(expectedSpan, diagnostic.Location.SourceSpan);

        var lineSpan = diagnostic.Location.GetLineSpan();
        Assert.Equal(path, lineSpan.Path);
        Assert.Equal(SourceText.From(source).Lines.GetLinePositionSpan(expectedSpan), lineSpan.Span);
    }
}
