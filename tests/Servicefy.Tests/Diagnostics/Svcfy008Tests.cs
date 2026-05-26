using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Diagnostics;

public class Svcfy008Tests
{
    [Fact]
    public void SVCFY008_DecoratorRegisteredWithAddScoped_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                [AddScoped]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY008");
    }

    [Fact]
    public void SVCFY008_DecoratorRegisteredWithAddTransient_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                [AddTransient]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY008");
    }

    [Fact]
    public void SVCFY008_DecoratorRegisteredWithAddSingleton_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                [AddSingleton]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY008");
    }

    [Fact]
    public void SVCFY008_DecoratorWithNoAddAttribute_DoesNotReport()
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

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY008");
    }

    [Fact]
    public void SVCFY008_DecoratorWithAddKeyedScoped_DoesNotReport()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                [AddKeyedScoped("my-key")]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY008");
    }

    [Fact]
    public void SVCFY008_OnlyAffectsDecoratorClass_NotImplementation()
    {
        // MyService is not a decorator, so [AddScoped] on it must NOT trigger SVCFY008
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

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY008");
    }

    [Fact]
    public void SVCFY008_MultipleDecorators_EachViolatingOneReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                public class MyService : IMyService { }

                [DecoratorFor<IMyService>]
                [AddScoped]
                public class LoggingDecorator : IMyService
                {
                    public LoggingDecorator(IMyService inner) { }
                }

                [DecoratorFor<IMyService>]
                public class CacheDecorator : IMyService
                {
                    public CacheDecorator(IMyService inner) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        var SVCFY008 = diagnostics.Where(d => d.Id == "SVCFY008").ToList();
        Assert.Single(SVCFY008);
    }
}
