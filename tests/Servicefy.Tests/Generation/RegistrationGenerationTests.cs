using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class RegistrationGenerationTests
{
    [Fact]
    public void AddScoped_SingleInterface_GeneratesServicefyExtensions()
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

        var (output, _) = CompilationHelper.RunGenerator(source);

        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.ServicefyExtensions"));
    }

    [Fact]
    public void AddScoped_SingleInterface_GeneratesAddScopedCall()
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

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddScoped", generatedSource);
        Assert.Contains("IMyService", generatedSource);
        Assert.Contains("MyService", generatedSource);
    }

    [Fact]
    public void AddSingleton_GeneratesAddSingletonCall()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddSingleton]
                public class MyService : IMyService { }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddSingleton", generatedSource);
    }

    [Fact]
    public void AddTransient_GeneratesAddTransientCall()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddTransient]
                public class MyService : IMyService { }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddTransient", generatedSource);
    }

    [Fact]
    public void AddScoped_WithExplicitServiceType_GeneratesCorrectRegistration()
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

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("IMyService", generatedSource);
        Assert.Contains("MyService", generatedSource);
    }

    [Fact]
    public void AddScoped_GenericSyntax_RegistersCorrectly()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped<IMyService>]
                public class MyService : IMyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("IMyService", generatedSource);
        Assert.Contains("MyService", generatedSource);
    }

    [Fact]
    public void NoAnnotatedClasses_DoesNotGenerateServicefyExtensions()
    {
        var source = """
            namespace TestAssembly
            {
                public class PlainClass { }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        Assert.Null(output.GetTypeByMetadataName("TestAssembly.ServicefyExtensions"));
    }
}
