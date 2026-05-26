using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class ByNamespaceGenerationTests
{
    [Fact]
    public void GeneratesConventionsBuilderRuntimeApi()
    {
        var source = """
            namespace TestAssembly
            {
                public class Marker { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.ServicefyConventionsBuilder"));
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.IServicefyConventionsBuilder"));
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.ServicefyConventionsExtensions"));
    }

    [Fact]
    public void ByNamespace_StartsWith_RegistersMatchingTypes()
    {
        var source = """
            namespace MY.APP.Example
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MY.APP.Other
            {
                public interface IBar { }
                public class Bar : IBar { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("MY.APP.Example"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MY.APP.Example.IFoo, global::MY.APP.Example.Foo>();", generatedSource);
        Assert.DoesNotContain("MY.APP.Other", generatedSource);
    }

    [Fact]
    public void ByNamespace_MatchesTypesFromReferencedProject()
    {
        var referencedSource = """
            namespace GRO.Server.Features._Seedings
            {
                public interface ISeedingService { }
                public class SeedingService : ISeedingService { }
            }
            """;

        var source = """
            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns == "GRO.Server.Features._Seedings", Lifetime.Transient);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGeneratorWithProjectReference(source, referencedSource);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains(
            "Services.AddTransient<global::GRO.Server.Features._Seedings.ISeedingService, global::GRO.Server.Features._Seedings.SeedingService>();",
            generatedSource);
    }

    [Fact]
    public void ByNamespace_NoMatchingTypes_DoesNotEmitImplementation()
    {
        var source = """
            namespace MY.APP.Other
            {
                public interface IBar { }
                public class Bar : IBar { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("MY.APP.Example"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespace_EndsWithArray_RegistersMatchingTypes()
    {
        var source = """
            namespace MY.APP.Services
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MY.APP.Repositories
            {
                public interface IBar { }
                public class Bar : IBar { }
            }

            namespace MY.APP.Models
            {
                public interface IBaz { }
                public class Baz : IBaz { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.EndsWith(["Services", "Repositories"]), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MY.APP.Services.IFoo, global::MY.APP.Services.Foo>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::MY.APP.Repositories.IBar, global::MY.APP.Repositories.Bar>();", generatedSource);
        Assert.DoesNotContain("Models", generatedSource);
        Assert.DoesNotContain("Baz", generatedSource);
    }

    [Fact]
    public void ByNamespace_EndsWithArrayWithVariableElement_ReportsSVCFY011AndIsIgnored()
    {
        var source = """
            namespace MY.APP.Services
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        string suffix = "Repositories";
                        services.AddServicefyConventions().ByNamespace(ns => ns.EndsWith(["Services", suffix]), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY011");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespace_UnsupportedPredicate_ReportsSVCFY012AndIsIgnored()
    {
        var source = """
            namespace MY.APP.Services
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.Length > 5, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY012");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespace_TruePredicate_MatchesEverything()
    {
        var source = """
            namespace MY.APP.Services
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(_ => true, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MY.APP.Services.IFoo, global::MY.APP.Services.Foo>();", generatedSource);
    }

    [Fact]
    public void ByNamespace_FalsePredicate_ReportsSVCFY012AndIsIgnored()
    {
        var source = """
            namespace MY.APP.Services
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(_ => false, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY012");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespace_MultipleInterfaces_RegistersAll()
    {
        var source = """
            namespace MY.APP.Example
            {
                public interface IServiceA { }
                public interface IServiceB { }
                public class MyService : IServiceA, IServiceB { }
            }
            
            namespace MY.APP.NotExample
            {
                public interface IServiceC { }
                public interface IServiceD { }
                public class OtherService : IServiceC, IServiceD { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("MY.APP.Example"), Lifetime.Singleton);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddSingleton<global::MY.APP.Example.IServiceA, global::MY.APP.Example.MyService>();", generatedSource);
        Assert.Contains("Services.AddSingleton<global::MY.APP.Example.IServiceB, global::MY.APP.Example.MyService>();", generatedSource);
        Assert.DoesNotContain("PlainClass", generatedSource);
        Assert.DoesNotContain("OtherService", generatedSource);
    }

    [Fact]
    public void ByNamespace_ClassWithNoInterfaces_IsNotRegistered()
    {
        var source = """
            namespace MY.APP.Example
            {
                public interface IFoo { }
                public class Foo : IFoo { }
                public class PlainClass { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("MY.APP.Example"), Lifetime.Transient);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddTransient<global::MY.APP.Example.IFoo, global::MY.APP.Example.Foo>();", generatedSource);
        Assert.DoesNotContain("PlainClass", generatedSource);
    }

    [Fact]
    public void ByNamespace_EndsWith_RegistersMatchingTypes()
    {
        var source = """
            namespace MY.APP.Example.Services
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MY.APP.Example.Models
            {
                public interface IBar { }
                public class Bar : IBar { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.EndsWith("Services"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MY.APP.Example.Services.IFoo, global::MY.APP.Example.Services.Foo>();", generatedSource);
        Assert.DoesNotContain("Models", generatedSource);
        Assert.DoesNotContain("Bar", generatedSource);
    }

    [Fact]
    public void ByNamespace_ClassWithExistingAddScoped_IsNotRegisteredAgain()
    {
        var source = """
            namespace MY.APP.Example
            {
                public interface IFoo { }

                [AddScoped]
                public class Foo : IFoo { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("MY.APP.Example"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespace_DecoratorClass_IsNotRegisteredAsPlainService()
    {
        var source = """
            namespace MY.APP.Example
            {
                public interface IUserService { }

                [AddScoped]
                public class UserService : IUserService { }

                [DecoratorFor<IUserService>]
                public class LoggingDecorator : IUserService
                {
                    public LoggingDecorator(IUserService inner) { }
                }

                public interface ICustomLogger { }

                [AddScoped]
                public class CustomLogger : ICustomLogger { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns == "MY.APP.Example", Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id is "SVCFY007" or "SVCFY008");
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespace_ClassImplementingFrameworkInterface_DoesNotRegisterFrameworkInterface()
    {
        var source = """
            namespace MY.APP.Example
            {
                public interface IUserService { }
                public class UserService : IUserService { }

                public class ShouldNotRegister : System.IDisposable
                {
                    public void Dispose() { }
                }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.EndsWith("Example"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MY.APP.Example.IUserService, global::MY.APP.Example.UserService>();", generatedSource);
        Assert.DoesNotContain("IDisposable", generatedSource);
        Assert.DoesNotContain("ShouldNotRegister", generatedSource);
    }

    [Fact]
    public void ByNamespace_InterfaceWithDecorate_RegistersFullDecoratorChain()
    {
        var source = """
            namespace MY.APP.Example
            {
                public interface IUserService { }

                public class UserService : IUserService { }

                [DecoratorFor<IUserService>]
                public class LoggingDecorator : IUserService
                {
                    public LoggingDecorator(IUserService inner) { }
                }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns == "MY.APP.Example", Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id is "SVCFY007" or "SVCFY008");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddKeyedScoped<global::MY.APP.Example.IUserService, global::MY.APP.Example.UserService>(\"__BASE__\");", generatedSource);
        Assert.Contains("\"MY.APP.Example.LoggingDecorator\"", generatedSource);
        Assert.Contains("new global::MY.APP.Example.LoggingDecorator(", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::MY.APP.Example.IUserService>(\"__BASE__\")", generatedSource);
        Assert.Contains("Services.AddScoped<global::MY.APP.Example.IUserService>(sp =>", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::MY.APP.Example.IUserService>(\"MY.APP.Example.LoggingDecorator\")", generatedSource);
    }

    [Fact]
    public void ByNamespace_InterfaceWithDecorateFromReferencedProject_RegistersFullDecoratorChain()
    {
        var referencedSource = """
            using System;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public sealed class DecoratorForAttribute<T> : Attribute { }

            namespace GRO.Server.Features._Seedings
            {
                public interface ISeedingService { }

                public class SeedingService : ISeedingService { }

                [DecoratorFor<ISeedingService>]
                public class LoggingDecorator : ISeedingService
                {
                    public LoggingDecorator(ISeedingService inner) { }
                }
            }
            """;

        var source = """
            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns == "GRO.Server.Features._Seedings", Lifetime.Transient);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGeneratorWithProjectReference(source, referencedSource);

        Assert.DoesNotContain(diagnostics, d => d.Id is "SVCFY007" or "SVCFY008");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddKeyedTransient<global::GRO.Server.Features._Seedings.ISeedingService, global::GRO.Server.Features._Seedings.SeedingService>(\"__BASE__\");", generatedSource);
        Assert.Contains("\"GRO.Server.Features._Seedings.LoggingDecorator\"", generatedSource);
        Assert.Contains("new global::GRO.Server.Features._Seedings.LoggingDecorator(", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::GRO.Server.Features._Seedings.ISeedingService>(\"__BASE__\")", generatedSource);
        Assert.Contains("Services.AddTransient<global::GRO.Server.Features._Seedings.ISeedingService>(sp =>", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::GRO.Server.Features._Seedings.ISeedingService>(\"GRO.Server.Features._Seedings.LoggingDecorator\")", generatedSource);
    }

    [Fact]
    public void ByNamespace_DecoratorDeclaredInUpstreamProject_RegistersFullDecoratorChain()
    {
        // "Abstractions": shared library declaring the interface, its [DecoratorFor<T>] attribute,
        // and the decorator implementation.
        var abstractionsSource = """
            using System;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public sealed class DecoratorForAttribute<T> : Attribute { }

            namespace Shared.Decorators
            {
                public interface ISeedingService { }

                [DecoratorFor<ISeedingService>]
                public class LoggingDecorator : ISeedingService
                {
                    public LoggingDecorator(ISeedingService inner) { }
                }
            }
            """;

        // "Features": references Abstractions, declares the base implementation that gets
        // registered by convention from the entry-point project.
        var featuresSource = """
            using Shared.Decorators;

            namespace GRO.Server.Features._Seedings
            {
                public class SeedingService : ISeedingService { }
            }
            """;

        // Entry point (e.g. GRO.Api): references Features (and transitively Abstractions).
        var source = """
            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns == "GRO.Server.Features._Seedings", Lifetime.Transient);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGeneratorWithProjectReferences(
            source,
            referencedProjects: [(abstractionsSource, "Abstractions"), (featuresSource, "Features")]);

        Assert.DoesNotContain(diagnostics, d => d.Id is "SVCFY007" or "SVCFY008");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddKeyedTransient<global::Shared.Decorators.ISeedingService, global::GRO.Server.Features._Seedings.SeedingService>(\"__BASE__\");", generatedSource);
        Assert.Contains("\"Shared.Decorators.LoggingDecorator\"", generatedSource);
        Assert.Contains("new global::Shared.Decorators.LoggingDecorator(", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::Shared.Decorators.ISeedingService>(\"__BASE__\")", generatedSource);
        Assert.Contains("Services.AddTransient<global::Shared.Decorators.ISeedingService>(sp =>", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::Shared.Decorators.ISeedingService>(\"Shared.Decorators.LoggingDecorator\")", generatedSource);
    }
}
