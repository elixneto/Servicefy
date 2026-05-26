using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class ByBaseTypeGenerationTests
{
    [Fact]
    public void GeneratesByBaseTypeRuntimeApi()
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
        Assert.Contains("ByBaseType<TBase>", builderSource);

        var selectorSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServiceTypeSelector.g.cs"))
            ?.ToString();

        Assert.NotNull(selectorSource);
        Assert.Contains("enum ServiceTypeSelector", selectorSource);
        Assert.Contains("BaseType = 0", selectorSource);
        Assert.Contains("ImplementedInterfaces", selectorSource);
        Assert.Contains("Self", selectorSource);
        Assert.Contains("SelfWithInterfaces", selectorSource);
    }

    [Fact]
    public void ByBaseType_DefaultSelector_RegistersAgainstBaseInterface()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IFoo { }
                public class Foo : IFoo { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("if (baseType == typeof(global::TestAssembly.IFoo) && lifetime == Lifetime.Scoped && selector == ServiceTypeSelector.BaseType && matchAttribute == null)", generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IFoo, global::TestAssembly.Foo>();", generatedSource);
    }

    [Fact]
    public void ByBaseType_ImplementedInterfaces_RegistersAllDirectInterfaces()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IFoo { }
                public interface IBar { }
                public class Foo : IFoo, IBar { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IFoo, global::TestAssembly.Foo>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IBar, global::TestAssembly.Foo>();", generatedSource);
    }

    [Fact]
    public void ByBaseType_ImplementedInterfaces_DoesNotRegisterFrameworkInterfaces()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IFoo { }
                public class Foo : IFoo, System.IDisposable
                {
                    public void Dispose() { }
                }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IFoo, global::TestAssembly.Foo>();", generatedSource);
        Assert.DoesNotContain("IDisposable", generatedSource);
    }

    [Fact]
    public void ByBaseType_Self_RegistersConcreteTypeOnly()
    {
        var source = """
            namespace TestAssembly
            {
                public abstract class FooBase { }
                public class Foo : FooBase { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<FooBase>(Lifetime.Scoped, ServiceTypeSelector.Self);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Foo>();", generatedSource);
        Assert.DoesNotContain("AddScoped<global::TestAssembly.FooBase,", generatedSource);
    }

    [Fact]
    public void ByBaseType_SelfSelectorWithInterfaceBaseType_ReportsSVCFY010()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IFoo { }
                public class Foo : IFoo { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped, ServiceTypeSelector.Self);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SVCFY010");
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByBaseType_SelfWithInterfaces_RegistersSelfAndFactoryPerInterface()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IFoo { }
                public class Foo : IFoo { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped, ServiceTypeSelector.SelfWithInterfaces);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Foo>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IFoo>(sp => sp.GetRequiredService<global::TestAssembly.Foo>());", generatedSource);
    }

    [Fact]
    public void ByBaseType_WithAttributeFilter_OnlyMatchesDecoratedTypes()
    {
        var source = """
            using System;

            namespace TestAssembly
            {
                [AttributeUsage(AttributeTargets.Class)]
                public class MyMarkerAttribute : Attribute { }

                public interface IFoo { }

                [MyMarker]
                public class FooA : IFoo { }

                public class FooB : IFoo { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped, matchAttribute: typeof(MyMarkerAttribute));
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IFoo, global::TestAssembly.FooA>();", generatedSource);
        Assert.DoesNotContain("FooB", generatedSource);
        Assert.Contains("matchAttribute == typeof(global::TestAssembly.MyMarkerAttribute)", generatedSource);
    }

    [Fact]
    public void ByBaseType_InterfaceWithDecorate_RegistersFullDecoratorChain()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IUserService { }

                public class UserService : IUserService { }

                [DecoratorFor<IUserService>]
                public class LoggingDecorator : IUserService
                {
                    public LoggingDecorator(IUserService inner) { }
                }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IUserService>(Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id is "SVCFY007" or "SVCFY008");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddKeyedScoped<global::TestAssembly.IUserService, global::TestAssembly.UserService>(\"__BASE__\");", generatedSource);
        Assert.Contains("\"TestAssembly.LoggingDecorator\"", generatedSource);
        Assert.Contains("new global::TestAssembly.LoggingDecorator(", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::TestAssembly.IUserService>(\"__BASE__\")", generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IUserService>(sp =>", generatedSource);
        Assert.Contains("sp.GetRequiredKeyedService<global::TestAssembly.IUserService>(\"TestAssembly.LoggingDecorator\")", generatedSource);
    }

    [Fact]
    public void ByBaseType_ClassWithExistingAddScoped_IsNotRegisteredAgain()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IFoo { }

                [AddScoped]
                public class Foo : IFoo { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByBaseType_MultipleImplementations_RegistersAllAgainstSameBase()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IService { }
                public class FooA : IService { }
                public class FooB : IService { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IService>(Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IService, global::TestAssembly.FooA>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IService, global::TestAssembly.FooB>();", generatedSource);
    }

    [Fact]
    public void ByBaseType_BaseTypeIsAbstractClass_RegistersDerivedClasses()
    {
        var source = """
            namespace TestAssembly
            {
                public abstract class JobBase { }
                public class JobA : JobBase { }
                public class JobB : JobBase { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<JobBase>(Lifetime.Singleton, ServiceTypeSelector.Self);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.JobA>();", generatedSource);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.JobB>();", generatedSource);
        Assert.DoesNotContain("JobBase>();", generatedSource);
    }

    [Fact]
    public void ByBaseType_NoMatchingTypes_DoesNotEmitImplementation()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IFoo { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.Null(generatedSource);
    }

    [Fact]
    public void ByBaseType_TransitiveInterface_IsMatchedViaAllInterfaces()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IBase { }
                public interface IDerived : IBase { }
                public class Foo : IDerived { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IBase>(Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IBase, global::TestAssembly.Foo>();", generatedSource);
    }

    [Fact]
    public void ByBaseType_TransitiveBaseClass_IsMatchedViaBaseTypeChain()
    {
        var source = """
            namespace TestAssembly
            {
                public abstract class C { }
                public class B : C { }
                public class A : B { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<C>(Lifetime.Scoped, ServiceTypeSelector.Self);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.A>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.B>();", generatedSource);
        Assert.DoesNotContain("global::TestAssembly.C>();", generatedSource);
    }

    [Fact]
    public void ByBaseType_MultipleDistinctCallSites_EmitSeparateDispatchBranches()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IFoo { }
                public interface IBar { }
                public class Foo : IFoo { }
                public class Bar : IBar { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IFoo>(Lifetime.Scoped);
                        services.AddServicefyConventions().ByBaseType<IBar>(Lifetime.Singleton);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("if (baseType == typeof(global::TestAssembly.IFoo) && lifetime == Lifetime.Scoped && selector == ServiceTypeSelector.BaseType && matchAttribute == null)", generatedSource);
        Assert.Contains("if (baseType == typeof(global::TestAssembly.IBar) && lifetime == Lifetime.Singleton && selector == ServiceTypeSelector.BaseType && matchAttribute == null)", generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IFoo, global::TestAssembly.Foo>();", generatedSource);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.IBar, global::TestAssembly.Bar>();", generatedSource);
    }

    [Fact]
    public void ByBaseType_MultipleImplementations_DecoratedInterface_SkipsDecoratorChain()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IUserService { }

                public class UserServiceA : IUserService { }
                public class UserServiceB : IUserService { }

                [DecoratorFor<IUserService>]
                public class LoggingDecorator : IUserService
                {
                    public LoggingDecorator(IUserService inner) { }
                }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IUserService>(Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id is "SVCFY007" or "SVCFY008");

        var generatedSource = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs"))
            ?.ToString();

        Assert.NotNull(generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IUserService, global::TestAssembly.UserServiceA>();", generatedSource);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IUserService, global::TestAssembly.UserServiceB>();", generatedSource);
        Assert.DoesNotContain("AddKeyedScoped", generatedSource);
    }
}
