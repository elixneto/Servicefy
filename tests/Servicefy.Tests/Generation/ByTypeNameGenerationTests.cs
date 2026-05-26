using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class ByTypeNameGenerationTests
{
    [Fact]
    public void GeneratesByTypeNameRuntimeApi()
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
        Assert.Contains("ByTypeName(", builderSource);
    }

    [Fact]
    public void ByTypeName_NameAndNamespace_RegistersMatchingTypes()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserRepository { }
                public class UserRepository : IUserRepository { }

                public interface IUserService { }
                public class UserService : IUserService { }
            }

            namespace MyApp.Other
            {
                public interface IOrderRepository { }
                public class OrderRepository : IOrderRepository { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith("Repository") && ns.Equals("MyApp.Implementations"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IUserRepository, global::MyApp.Implementations.UserRepository>();", generatedSource);
        Assert.DoesNotContain("UserService", generatedSource);
        Assert.DoesNotContain("OrderRepository", generatedSource);
    }

    [Fact]
    public void ByTypeName_OrPredicate_RegistersEitherMatch()
    {
        var source = """
            namespace MyApp.Example
            {
                public interface IFoo { }
                public class FooHandler : IFoo { }

                public interface IBar { }
                public class BarHandler : IBar { }

                public interface IBaz { }
                public class BazWorker : IBaz { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => name.StartsWith("Foo") || name.StartsWith("Bar"), Lifetime.Singleton);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddSingleton<global::MyApp.Example.IFoo, global::MyApp.Example.FooHandler>();", generatedSource);
        Assert.Contains("Services.AddSingleton<global::MyApp.Example.IBar, global::MyApp.Example.BarHandler>();", generatedSource);
        Assert.DoesNotContain("BazWorker", generatedSource);
    }

    [Fact]
    public void ByTypeName_UnsupportedPredicate_ReportsSVCFY012AndIsIgnored()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserRepository { }
                public class UserRepository : IUserRepository { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => name.Length > 5, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY012");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByTypeName_TruePredicate_MatchesEverything()
    {
        var source = """
            namespace MyApp.Implementations
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
                        services.AddServicefyConventions().ByTypeName((_, _) => true, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IFoo, global::MyApp.Implementations.Foo>();", generatedSource);
    }

    [Fact]
    public void ByTypeName_FalsePredicate_ReportsSVCFY012AndIsIgnored()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserRepository { }
                public class UserRepository : IUserRepository { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((_, _) => false, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY012");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByTypeName_NoMatchingTypes_DoesNotEmitImplementation()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserService { }
                public class UserService : IUserService { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith("Repository"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByTypeName_InterfaceWithDecorate_RegistersFullDecoratorChain()
    {
        var source = """
            namespace MyApp.Implementations
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
                        services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith("Repository"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id is "SVCFY007" or "SVCFY008");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddKeyedScoped<global::MyApp.Implementations.IUserRepository, global::MyApp.Implementations.UserRepository>(\"__BASE__\");", generatedSource);
        Assert.Contains("new global::MyApp.Implementations.LoggingDecorator(", generatedSource);
    }

    [Fact]
    public void ByTypeName_EndsWithArray_RegistersMatchingTypes()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserRepository { }
                public class UserRepository : IUserRepository { }

                public interface IUserHandler { }
                public class UserHandler : IUserHandler { }

                public interface IUserQuery { }
                public class UserQuery : IUserQuery { }

                public interface IUserWorker { }
                public class UserWorker : IUserWorker { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => ns.StartsWith("MyApp.") && name.EndsWith(["Repository", "Handler", "Query"]), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IUserRepository, global::MyApp.Implementations.UserRepository>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IUserHandler, global::MyApp.Implementations.UserHandler>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IUserQuery, global::MyApp.Implementations.UserQuery>();", generatedSource);
        Assert.DoesNotContain("UserWorker", generatedSource);
    }

    [Fact]
    public void ByTypeName_EndsWithParams_RegistersMatchingTypes()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserRepository { }
                public class UserRepository : IUserRepository { }

                public interface IUserHandler { }
                public class UserHandler : IUserHandler { }

                public interface IUserWorker { }
                public class UserWorker : IUserWorker { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => ns.StartsWith("MyApp.") && name.EndsWith("Repository", "Handler"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IUserRepository, global::MyApp.Implementations.UserRepository>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IUserHandler, global::MyApp.Implementations.UserHandler>();", generatedSource);
        Assert.DoesNotContain("UserWorker", generatedSource);
    }

    [Fact]
    public void ByTypeName_EndsWithParamsWithLeadingComparison_RegistersMatchingTypes()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserRepository { }
                public class UserRepository : IUserRepository { }

                public interface IUserHANDLER { }
                public class UserHANDLER : IUserHANDLER { }
            }

            namespace TestAssembly
            {
                using System;

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith(StringComparison.OrdinalIgnoreCase, "Repository", "Handler"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IUserRepository, global::MyApp.Implementations.UserRepository>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::MyApp.Implementations.IUserHANDLER, global::MyApp.Implementations.UserHANDLER>();", generatedSource);
    }

    [Fact]
    public void ByTypeName_EndsWithParamsWithNonLiteralElement_ReportsSVCFY011AndIsIgnored()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserRepository { }
                public class UserRepository : IUserRepository { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    private const string HandlerSuffix = "Handler";

                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith("Repository", HandlerSuffix), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY011");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByTypeName_EndsWithArrayWithNonLiteralElement_ReportsSVCFY011AndIsIgnored()
    {
        var source = """
            namespace MyApp.Implementations
            {
                public interface IUserRepository { }
                public class UserRepository : IUserRepository { }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    private const string HandlerSuffix = "Handler";

                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith(["Repository", HandlerSuffix]), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY011");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByTypeName.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }
}
