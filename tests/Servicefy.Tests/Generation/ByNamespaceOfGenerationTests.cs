using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class ByNamespaceOfGenerationTests
{
    [Fact]
    public void GeneratesByNamespaceOfRuntimeApi()
    {
        var source = """
            namespace TestAssembly
            {
                public class Marker { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);

        var builderSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.g.cs"))
            ?.ToString();

        Assert.NotNull(builderSource);
        Assert.Contains("ByNamespaceOf<TMarker>", builderSource);
    }

    [Fact]
    public void ByNamespaceOf_UnsupportedPredicate_ReportsSVCFY012AndIsIgnored()
    {
        var source = """
            namespace MyApp.Data
            {
                public class Marker { }
            }

            namespace MyApp.Data.Repositories
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
                        services.AddServicefyConventions().ByNamespaceOf<MyApp.Data.Marker>(ns => ns.Length > 5, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY012");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespaceOf.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespaceOf_TruePredicate_MatchesEverythingInMarkerNamespace()
    {
        var source = """
            namespace MyApp.Data
            {
                public class Marker { }
            }

            namespace MyApp.Data.Repositories
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MyApp.Other.Repositories
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
                        services.AddServicefyConventions().ByNamespaceOf<MyApp.Data.Marker>(_ => true, Lifetime.Transient);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespaceOf.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddTransient<global::MyApp.Data.Repositories.IFoo, global::MyApp.Data.Repositories.Foo>();", generatedSource);
        Assert.DoesNotContain("MyApp.Other", generatedSource);
        Assert.DoesNotContain("Bar", generatedSource);
    }

    [Fact]
    public void ByNamespaceOf_FalsePredicate_ReportsSVCFY012AndIsIgnored()
    {
        var source = """
            namespace MyApp.Data
            {
                public class Marker { }
            }

            namespace MyApp.Data.Repositories
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
                        services.AddServicefyConventions().ByNamespaceOf<MyApp.Data.Marker>(_ => false, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY012");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespaceOf.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespaceOf_RestrictsMatchingToMarkerNamespace()
    {
        var source = """
            namespace MyApp.Data
            {
                public class Marker { }
            }

            namespace MyApp.Data.Repositories
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MyApp.Other.Repositories
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
                        services.AddServicefyConventions().ByNamespaceOf<MyApp.Data.Marker>(ns => ns.Contains("Repositories"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespaceOf.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Data.Repositories.IFoo, global::MyApp.Data.Repositories.Foo>();", generatedSource);
        Assert.DoesNotContain("MyApp.Other", generatedSource);
        Assert.DoesNotContain("Bar", generatedSource);
    }

    [Fact]
    public void ByNamespaceOf_MarkerNamespaceItself_IsCandidate()
    {
        var source = """
            namespace MyApp.Data
            {
                public class Marker { }

                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespaceOf<MyApp.Data.Marker>(ns => ns == "MyApp.Data", Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespaceOf.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Data.IFoo, global::MyApp.Data.Foo>();", generatedSource);
    }

    [Fact]
    public void ByNamespaceOf_NoMatchingTypes_DoesNotEmitImplementation()
    {
        var source = """
            namespace MyApp.Data
            {
                public class Marker { }
            }

            namespace MyApp.Data.Repositories
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
                        services.AddServicefyConventions().ByNamespaceOf<MyApp.Data.Marker>(ns => ns.EndsWith(".Handlers"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespaceOf.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByNamespaceOf_InterfaceWithDecorate_RegistersFullDecoratorChain()
    {
        var source = """
            namespace MyApp.Data
            {
                public class Marker { }
            }

            namespace MyApp.Data.Repositories
            {
                public interface IUserRepository { }

                public class UserRepository : IUserRepository { }

                [DecoratorFor<IUserRepository>]
                public class LoggingDecorator : IUserRepository
                {
                    public LoggingDecorator(IUserRepository inner) { }
                }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespaceOf<MyApp.Data.Marker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id is "SVCFY007" or "SVCFY008");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespaceOf.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddKeyedScoped<global::MyApp.Data.Repositories.IUserRepository, global::MyApp.Data.Repositories.UserRepository>(\"__BASE__\");", generatedSource);
        Assert.Contains("new global::MyApp.Data.Repositories.LoggingDecorator(", generatedSource);
    }
}
