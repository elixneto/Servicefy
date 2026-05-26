using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Diagnostics;

public class DiagnosticsTests
{
    [Fact]
    public void SVCFY001_AddAttribute_WithoutLifetime_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [Add]
                public class MyService : IMyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY001");
    }

    [Fact]
    public void SVCFY001_AddScopedAttribute_DoesNotReport()
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

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY001");
    }

    [Fact]
    public void SVCFY002_ExplicitServiceType_NotImplemented_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }
                public interface IOtherService { }

                [AddScoped(typeof(IOtherService))]
                public class MyService : IMyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY002");
    }

    [Fact]
    public void SVCFY002_ExplicitServiceType_Implemented_DoesNotReport()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped(typeof(IMyService))]
                public class MyService : IMyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY002");
    }

    [Fact]
    public void SVCFY002_Keyed_ExplicitServiceType_NotImplemented_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }
                public interface IOtherService { }

                [AddKeyedScoped("key", typeof(IOtherService))]
                public class MyService : IMyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY002");
    }

    [Fact]
    public void SVCFY003_NoInterface_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddScoped]
                public class MyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY003");
    }

    [Fact]
    public void SVCFY003_Keyed_NoInterface_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddKeyedScoped("key")]
                public class MyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY003");
    }

    [Fact]
    public void SVCFY001_AddSelf_WithoutLifetime_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelf]
                public class MyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY001");
    }

    [Fact]
    public void SVCFY001_AddSelf_WithLifetime_DoesNotReport()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelf(Lifetime.Scoped)]
                public class MyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY001");
    }

    [Fact]
    public void SVCFY009_AddSelf_AbstractClass_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelf(Lifetime.Scoped)]
                public abstract class MyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY009");
    }

    [Fact]
    public void SVCFY009_AddSelfScoped_AbstractClass_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelfScoped]
                public abstract class MyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY009");
    }

    [Fact]
    public void SVCFY009_AddSelfScoped_StaticClass_ReportsError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelfScoped]
                public static class MyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY009");
    }

    [Fact]
    public void SVCFY009_AddSelfScoped_ConcreteClass_DoesNotReport()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelfScoped]
                public class MyService { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SVCFY009");
    }

    [Fact]
    public void MultipleInterfaces_NoExplicitType_DoesNotReportError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IServiceA { }
                public interface IServiceB { }

                [AddScoped]
                public class MyService : IServiceA, IServiceB { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Keyed_MultipleInterfaces_NoExplicitType_DoesNotReportError()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IServiceA { }
                public interface IServiceB { }

                [AddKeyedScoped("key")]
                public class MyService : IServiceA, IServiceB { }
            }
            """;

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
    }
}
