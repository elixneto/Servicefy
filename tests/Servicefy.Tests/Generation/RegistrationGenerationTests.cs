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
    public void AddScoped_MultipleInterfaces_NoExplicitType_RegistersAllInterfaces()
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

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.AddScoped<global::TestAssembly.IServiceA, global::TestAssembly.MyService>();", generatedSource);
        Assert.Contains("services.AddScoped<global::TestAssembly.IServiceB, global::TestAssembly.MyService>();", generatedSource);
    }

    [Fact]
    public void AddSelf_WithLifetime_GeneratesSelfRegistration()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelf(Lifetime.Scoped)]
                public class MyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.AddScoped<global::TestAssembly.MyService>();", generatedSource);
    }

    [Fact]
    public void AddSelfScoped_GeneratesSelfRegistration()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelfScoped]
                public class MyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.AddScoped<global::TestAssembly.MyService>();", generatedSource);
    }

    [Fact]
    public void AddSelfSingleton_GeneratesSelfRegistration()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelfSingleton]
                public class MyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.AddSingleton<global::TestAssembly.MyService>();", generatedSource);
    }

    [Fact]
    public void AddSelfTransient_GeneratesSelfRegistration()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [AddSelfTransient]
                public class MyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.AddTransient<global::TestAssembly.MyService>();", generatedSource);
    }

    [Fact]
    public void AddSelfScoped_WithInterfaces_DoesNotRegisterInterfaces()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddSelfScoped]
                public class MyService : IMyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.AddScoped<global::TestAssembly.MyService>();", generatedSource);
        Assert.DoesNotContain("IMyService", generatedSource);
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

    [Fact]
    public void NoConfigureAttribute_AddServicefyHasNoConfigurationParameter()
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
        Assert.Contains("AddServicefy(this IServiceCollection services)", generatedSource);
        Assert.DoesNotContain("IConfiguration", generatedSource);
        Assert.DoesNotContain("Microsoft.Extensions.Configuration", generatedSource);
    }

    [Fact]
    public void ConfigureAttribute_AddServicefyHasConfigurationParameter()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [Configure("MySection", Lifetime.Singleton)]
                public class MyOptions
                {
                    public MyOptions() { }
                }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("AddServicefy(this IServiceCollection services, IConfiguration configuration)", generatedSource);
        Assert.Contains("Microsoft.Extensions.Configuration", generatedSource);
    }

    [Fact]
    public void ConfigureAttribute_Singleton_BindsSectionDirectly()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                [Configure("MySection", Lifetime.Singleton)]
                public class MyOptions
                {
                    public MyOptions() { }
                }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.AddSingleton(_ => configuration.GetSection(\"MySection\").Get<global::TestAssembly.MyOptions>()", generatedSource);
        Assert.DoesNotContain("Configure<global::TestAssembly.MyOptions>", generatedSource);
        Assert.DoesNotContain("IOptionsMonitor", generatedSource);
        Assert.DoesNotContain("Microsoft.Extensions.Options", generatedSource);
    }

    [Theory]
    [InlineData("Scoped")]
    [InlineData("Transient")]
    public void ConfigureAttribute_ScopedOrTransient_UsesOptionsMonitorForReload(string lifetime)
    {
        var source = $$"""
            using TestAssembly;
            namespace TestAssembly
            {
                [Configure("MySection", Lifetime.{{lifetime}})]
                public class MyOptions
                {
                    public MyOptions() { }
                }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("services.Configure<global::TestAssembly.MyOptions>(configuration.GetSection(\"MySection\"));", generatedSource);
        Assert.Contains($"services.Add{lifetime}(sp => sp.GetRequiredService<IOptionsMonitor<global::TestAssembly.MyOptions>>().Value);", generatedSource);
        Assert.Contains("Microsoft.Extensions.Options", generatedSource);
    }
}
