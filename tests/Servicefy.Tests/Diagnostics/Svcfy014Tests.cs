using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Diagnostics;

public class Svcfy014Tests
{
    [Fact]
    public void SVCFY014_DecoratorImplementingTwoInterfaces_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }
                public interface IOtherService { }

                [AddScoped]
                public class MyService : IMyService { }

                [Decorator]
                public class LoggingDecorator : IMyService, IOtherService
                {
                    public LoggingDecorator(IMyService inner) { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY014");
    }

    [Fact]
    public void SVCFY014_DecoratorImplementingNoInterfaces_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [Decorator]
                public class LoggingDecorator
                {
                    public LoggingDecorator() { }
                }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY014");
    }

    [Fact]
    public void SVCFY014_DecoratorImplementingExactlyOneInterface_DoesNotReport()
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

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY014");
    }
}
