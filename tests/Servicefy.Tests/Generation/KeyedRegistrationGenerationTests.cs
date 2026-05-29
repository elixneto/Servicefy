using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class KeyedRegistrationGenerationTests
{
    [Fact]
    public void AddKeyedScoped_SingleInterface_GeneratesServicefyExtensions()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddKeyedScoped("myKey")]
                public class MyService : IMyService { }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.ServicefyExtensions"));
    }

    [Fact]
    public void AddKeyedScoped_GeneratesAddKeyedScopedCall()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddKeyedScoped("myKey")]
                public class MyService : IMyService { }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedScoped", generatedSource);
        Assert.Contains("IMyService", generatedSource);
        Assert.Contains("MyService", generatedSource);
        Assert.Contains("\"myKey\"", generatedSource);
    }

    [Fact]
    public void AddKeyedSingleton_GeneratesAddKeyedSingletonCall()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddKeyedSingleton("cache")]
                public class MyService : IMyService { }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedSingleton", generatedSource);
        Assert.Contains("\"cache\"", generatedSource);
    }

    [Fact]
    public void AddKeyedTransient_GeneratesAddKeyedTransientCall()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddKeyedTransient("handler")]
                public class MyService : IMyService { }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedTransient", generatedSource);
        Assert.Contains("\"handler\"", generatedSource);
    }

    [Fact]
    public void AddKeyedScoped_WithExplicitServiceType_GeneratesCorrectRegistration()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddKeyedScoped("myKey", typeof(IMyService))]
                public class MyService : IMyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedScoped", generatedSource);
        Assert.Contains("IMyService", generatedSource);
        Assert.Contains("\"myKey\"", generatedSource);
    }

    [Fact]
    public void AddKeyedScoped_GenericSyntax_GeneratesCorrectRegistration()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddKeyedScoped<IMyService>("myKey")]
                public class MyService : IMyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedScoped", generatedSource);
        Assert.Contains("IMyService", generatedSource);
        Assert.Contains("\"myKey\"", generatedSource);
    }

    [Fact]
    public void AddKeyedScoped_IntKey_GeneratesCorrectRegistration()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddKeyedScoped(42)]
                public class MyService : IMyService { }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddKeyedScoped", generatedSource);
        Assert.Contains("42", generatedSource);
    }

    [Fact]
    public void RegularAndKeyed_BothGenerated()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped]
                [AddKeyedScoped("named")]
                public class MyService : IMyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddScoped", generatedSource);
        Assert.Contains("AddKeyedScoped", generatedSource);
        Assert.Contains("\"named\"", generatedSource);
    }
}
