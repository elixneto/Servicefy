using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class DecoratorGenerationTests
{
    [Fact]
    public void SingleDecorator_GeneratesKeyedBaseAndFactory()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedScoped", generatedSource);
        Assert.Contains("\"__BASE__\"", generatedSource);
        Assert.Contains("GetRequiredKeyedService", generatedSource);
        Assert.Contains("LoggingDecorator", generatedSource);
        Assert.DoesNotContain("services.AddScoped<global::TestAssembly.IMyService, global::TestAssembly.MyService>()", generatedSource);
    }

    [Fact]
    public void SingleDecorator_FactoryUsesNewExpressionWithInner()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("new global::TestAssembly.LoggingDecorator(", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::TestAssembly.IMyService>(\"__BASE__\")", generatedSource);
    }

    [Fact]
    public void MultipleDecorators_OutermostFirst_ChainIsCorrect()
    {
        var source = """
            using TestAssembly;
            using Microsoft.Extensions.DependencyInjection;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
                public class CacheDecorator : IMyService
                {
                    public CacheDecorator(IMyService inner) { }
                }
                public class UmDecorator : IMyService
                {
                    public UmDecorator(IMyService inner) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Decorate<IMyService, LoggingDecorator>();
                        services.Decorate<IMyService, CacheDecorator>();
                        services.Decorate<IMyService, UmDecorator>();
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);

        var umIdx      = generatedSource!.IndexOf("UmDecorator", StringComparison.Ordinal);
        var cacheIdx   = generatedSource.IndexOf("CacheDecorator", StringComparison.Ordinal);
        var loggingIdx = generatedSource.IndexOf("LoggingDecorator", StringComparison.Ordinal);

        // innermost (UmDecorator, declared last) is emitted before CacheDecorator, which is before
        // LoggingDecorator (declared first = outermost)
        Assert.True(umIdx < cacheIdx, "UmDecorator should appear before CacheDecorator");
        Assert.True(cacheIdx < loggingIdx, "CacheDecorator should appear before LoggingDecorator");

        // non-keyed factory should point to outermost (LoggingDecorator)
        Assert.Contains("\"TestAssembly.LoggingDecorator\"", generatedSource);
        var lastKeyedRef = generatedSource.LastIndexOf("TestAssembly.LoggingDecorator", StringComparison.Ordinal);
        Assert.True(lastKeyedRef >= 0);
    }

    [Fact]
    public void ChainedFluentDecorate_FirstCallIsOutermost_ChainIsCorrect()
    {
        // Both .Decorate<,>() calls are chained off the same `services` receiver, so their
        // invocation spans share the same Start position — only End differs. The first call
        // in declaration order (CacheDecorator) must still be treated as the outermost layer.
        var source = """
            using TestAssembly;
            using Microsoft.Extensions.DependencyInjection;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                public class CacheDecorator : IMyService
                {
                    public CacheDecorator(IMyService inner) { }
                }
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Decorate<IMyService, CacheDecorator>()
                            .Decorate<IMyService, LoggingDecorator>();
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);

        var cacheIdx   = generatedSource!.IndexOf("CacheDecorator", StringComparison.Ordinal);
        var loggingIdx = generatedSource.IndexOf("LoggingDecorator", StringComparison.Ordinal);

        // innermost (LoggingDecorator, declared last) is emitted before CacheDecorator
        // (declared first = outermost)
        Assert.True(loggingIdx < cacheIdx, "LoggingDecorator should appear before CacheDecorator");

        // non-keyed factory should point to the outermost layer (CacheDecorator)
        var lastKeyedRef = generatedSource.LastIndexOf("TestAssembly.CacheDecorator", StringComparison.Ordinal);
        Assert.True(lastKeyedRef >= 0);
        Assert.Contains("GetRequiredKeyedService<global::TestAssembly.IMyService>(\"TestAssembly.CacheDecorator\")", generatedSource);
    }

    [Fact]
    public void MultipleDecorators_EachLayerUsesCorrectInnerKey()
    {
        var source = """
            using TestAssembly;
            using Microsoft.Extensions.DependencyInjection;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
                public class UmDecorator : IMyService
                {
                    public UmDecorator(IMyService inner) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Decorate<IMyService, LoggingDecorator>();
                        services.Decorate<IMyService, UmDecorator>();
                    }
                }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        // UmDecorator (innermost, declared last) takes __BASE__
        Assert.Contains("GetRequiredKeyedService<global::TestAssembly.IMyService>(\"__BASE__\")", generatedSource);
        // LoggingDecorator (outermost, declared first) takes UmDecorator key
        Assert.Contains("GetRequiredKeyedService<global::TestAssembly.IMyService>(\"TestAssembly.UmDecorator\")", generatedSource);
    }

    [Fact]
    public void Decorator_WithExtraConstructorParams_GeneratesGetRequiredService()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                public interface ILogger { }

                [DecoratorFor<IMyService>]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner, ILogger logger) { }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::TestAssembly.IMyService>(\"__BASE__\")", generatedSource);
        Assert.Contains("sp.GetRequiredService<global::TestAssembly.ILogger>()", generatedSource);
    }

    [Fact]
    public void SVCFY007_DecoratorWithoutServiceParameter_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                public class BadDecorator : IMyService
                {
                    public BadDecorator(string notTheService) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY007");
    }

    [Fact]
    public void SVCFY007_ValidDecorator_DoesNotReport()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                public class GoodDecorator : IMyService
                {
                    public GoodDecorator(IMyService inner) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");
    }

    [Fact]
    public void Decorator_WithNonInjectableOverload_PrefersInjectableConstructor()
    {
        // CacheDecorator has two constructors: one with (ICustomLogger, IMyService) and one
        // with (ICustomLogger, IMyService, string). The generator must pick the injectable
        // overload and not emit sp.GetRequiredService<string>() which would fail at runtime.
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                public interface ICustomLogger { }

                [DecoratorFor<IMyService>]
                public class CacheDecorator : IMyService
                {
                    public CacheDecorator(ICustomLogger logger, IMyService service) { }
                    public CacheDecorator(ICustomLogger logger, IMyService service, string text) { }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.DoesNotContain("GetRequiredService<string>", generatedSource);
        Assert.Contains("sp.GetRequiredService<global::TestAssembly.ICustomLogger>()", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::TestAssembly.IMyService>(\"__BASE__\")", generatedSource);
    }

    [Fact]
    public void Decorator_ActivatorUtilitiesConstructor_OverridesInjectableHeuristic()
    {
        // Even if a constructor has a non-injectable param, [ActivatorUtilitiesConstructor] forces its selection.
        var source = """
            using TestAssembly;
            using Microsoft.Extensions.DependencyInjection;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                public interface ICustomLogger { }

                [DecoratorFor<IMyService>]
                public class CacheDecorator : IMyService
                {
                    [ActivatorUtilitiesConstructor]
                    public CacheDecorator(ICustomLogger logger, IMyService service, string text) { }
                    public CacheDecorator(ICustomLogger logger, IMyService service) { }
                }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        // Should have used the 3-param ctor as instructed by [ActivatorUtilitiesConstructor]
        Assert.Contains("GetRequiredService<string>", generatedSource);
    }

    [Fact]
    public void NoDecorator_RegularRegistrationIsUnaffected()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.AddScoped<global::TestAssembly.IMyService, global::TestAssembly.MyService>();", generatedSource);
        Assert.DoesNotContain("__BASE__", generatedSource);
    }

    [Fact]
    public void FluentDecorate_WithoutAttribute_GeneratesKeyedChain()
    {
        var source = """
            using TestAssembly;
            using Microsoft.Extensions.DependencyInjection;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Decorate<IMyService, LoggingDecorator>();
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedScoped", generatedSource);
        Assert.Contains("\"__BASE__\"", generatedSource);
        Assert.Contains("LoggingDecorator", generatedSource);
    }

    [Fact]
    public void MergedDecorators_FluentDeclaresOuterLayers_AttributeFormsInnerLayer()
    {
        // CacheDecorator: [DecoratorFor<IMyService>] -> inner/base layer.
        // LoggingDecorator + AuditDecorator: .Decorate<,>() calls, declaration order, first = outermost.
        // Expected outermost -> innermost: LoggingDecorator, AuditDecorator, CacheDecorator.
        var source = """
            using TestAssembly;
            using Microsoft.Extensions.DependencyInjection;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                public class CacheDecorator : IMyService
                {
                    public CacheDecorator(IMyService inner) { }
                }

                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }

                public class AuditDecorator : IMyService
                {
                    public AuditDecorator(IMyService inner) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Decorate<IMyService, LoggingDecorator>();
                        services.Decorate<IMyService, AuditDecorator>();
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);

        var cacheIdx   = generatedSource!.IndexOf("CacheDecorator", StringComparison.Ordinal);
        var auditIdx   = generatedSource.IndexOf("AuditDecorator", StringComparison.Ordinal);
        var loggingIdx = generatedSource.IndexOf("LoggingDecorator", StringComparison.Ordinal);

        // emission is innermost-first: CacheDecorator (innermost), then AuditDecorator, then LoggingDecorator (outermost)
        Assert.True(cacheIdx < auditIdx, "CacheDecorator should appear before AuditDecorator");
        Assert.True(auditIdx < loggingIdx, "AuditDecorator should appear before LoggingDecorator");

        // non-keyed resolver points at the outermost layer (LoggingDecorator)
        Assert.Contains("\"TestAssembly.LoggingDecorator\"", generatedSource);
        // CacheDecorator (innermost) wraps __BASE__
        Assert.Contains("GetRequiredKeyedService<global::TestAssembly.IMyService>(\"__BASE__\")", generatedSource);
    }

    [Fact]
    public void SVCFY013_DecoratorForAttribute_TargetingInterfaceItDoesNotImplement_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IService001 { }
                public interface IService002 { }

                [AddScoped]
                public class Service001 : IService001 { }

                [DecoratorFor<IService001>]
                public class Service002Decorator : IService002
                {
                    public Service002Decorator(IService001 inner) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY013");
    }

    [Fact]
    public void DecoratorAttribute_SingleInterface_GeneratesKeyedBaseAndFactory()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [Decorator]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");
        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY014");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedScoped", generatedSource);
        Assert.Contains("\"__BASE__\"", generatedSource);
        Assert.Contains("GetRequiredKeyedService", generatedSource);
        Assert.Contains("LoggingDecorator", generatedSource);
    }

    [Fact]
    public void DecoratorAttribute_MergesWithDecoratorForAttribute_SortedByFqn()
    {
        // [Decorator] (inferred IMyService) and [DecoratorFor<IMyService>] both form the
        // inner/base layer and are sorted together by fully-qualified type name.
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                public class UmDecorator : IMyService
                {
                    public UmDecorator(IMyService inner) { }
                }

                [Decorator]
                public class AuditDecorator : IMyService
                {
                    public AuditDecorator(IMyService inner) { }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY007");
        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY014");

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);

        // FQN-sorted ascending: ["TestAssembly.AuditDecorator", "TestAssembly.UmDecorator"], emitted
        // in reverse (innermost first) -> UmDecorator (last in sorted order) is innermost.
        var auditIdx = generatedSource!.IndexOf("AuditDecorator", StringComparison.Ordinal);
        var umIdx    = generatedSource.IndexOf("UmDecorator", StringComparison.Ordinal);

        Assert.True(umIdx < auditIdx, "UmDecorator should appear before AuditDecorator");
        Assert.Contains("GetRequiredKeyedService<global::TestAssembly.IMyService>(\"__BASE__\")", generatedSource);
    }

    [Fact]
    public void SVCFY013_FluentDecorate_TargetingInterfaceItDoesNotImplement_ReportsError()
    {
        var source = """
            using TestAssembly;
            using Microsoft.Extensions.DependencyInjection;
            namespace TestAssembly
            {
                public interface IService001 { }
                public interface IService002 { }

                [AddScoped]
                public class Service001 : IService001 { }

                public class Service002Decorator : IService002
                {
                    public Service002Decorator(IService001 inner) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Decorate<IService001, Service002Decorator>();
                    }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY013");
    }

    [Fact]
    public void SVCFY015_DecoratorDeclaredViaAttributeAndFluent_ReportsWarningAndDedupes()
    {
        var source = """
            using TestAssembly;
            using Microsoft.Extensions.DependencyInjection;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [Decorator]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }

                public static class Composition
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Decorate<IMyService, LoggingDecorator>();
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        var warning = Assert.Single(diagnostics, d => d.Id == "SVCFY015");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);

        // LoggingDecorator must be registered exactly once under its keyed slot -
        // a duplicate registration with the same key would make its own factory
        // resolve itself, causing infinite recursion at runtime.
        var keyedRegistrationCount = generatedSource!
            .Split("AddKeyedScoped<global::TestAssembly.IMyService>(")
            .Length - 1;

        Assert.Equal(1, keyedRegistrationCount);
    }
}
