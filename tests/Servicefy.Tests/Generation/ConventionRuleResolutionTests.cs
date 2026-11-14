using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class ConventionRuleResolutionTests
{
    private const string ByBaseTypeHint = "ServicefyConventionsBuilder.ByBaseType.g.cs";

    private static string Startup(params string[] calls) => $$"""

        namespace TestAssembly
        {
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
        {{string.Join("\n", calls.Select(c => "            services.AddServicefyConventions()." + c + ";"))}}
                }
            }
        }
        """;

    private static string Generated(Compilation output, string hint) =>
        output.SyntaxTrees.FirstOrDefault(t => t.FilePath.EndsWith(hint, StringComparison.Ordinal))?.ToString()
        ?? throw new Xunit.Sdk.XunitException($"'{hint}' was not generated.");

    [Fact]
    public void ByBaseType_ClosedGenericWithNestedTypeArgument_Registers()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IRepository<T> { }
                public static class Outer { public class Order { } }
                public class OrderRepository : IRepository<Outer.Order> { }
                public class OtherRepository : IRepository<string> { }
            }
            """ + Startup("ByBaseType<IRepository<Outer.Order>>(Lifetime.Scoped)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("baseType == typeof(global::TestAssembly.IRepository<global::TestAssembly.Outer.Order>)", generated);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IRepository<global::TestAssembly.Outer.Order>, global::TestAssembly.OrderRepository>();", generated);
        Assert.DoesNotContain("OtherRepository", generated);
    }

    [Fact]
    public void ByBaseType_ClosedGenericWithFrameworkTypeArgument_Registers()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IRepository<T> { }
                public class StringRepository : IRepository<string> { }
                public class IntRepository : IRepository<int> { }
            }
            """ + Startup("ByBaseType<IRepository<string>>(Lifetime.Transient)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("Services.AddTransient<global::TestAssembly.IRepository<string>, global::TestAssembly.StringRepository>();", generated);
        Assert.DoesNotContain("IntRepository", generated);
    }

    [Fact]
    public void ByBaseType_ClosedGenericWithTwoTypeArguments_Registers()
    {
        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public class OrderDto { }
                public interface IMapper<TSource, TTarget> { }
                public class OrderMapper : IMapper<Order, OrderDto> { }
                public class ReverseMapper : IMapper<OrderDto, Order> { }
            }
            """ + Startup("ByBaseType<IMapper<Order, OrderDto>>(Lifetime.Singleton)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.IMapper<global::TestAssembly.Order, global::TestAssembly.OrderDto>, global::TestAssembly.OrderMapper>();", generated);
        Assert.DoesNotContain("ReverseMapper", generated);
    }

    [Fact]
    public void ByBaseType_ClosedGenericWithGenericTypeArgument_Registers()
    {
        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public class Envelope<T> { }
                public interface IHandler<T> { }
                public class EnvelopeHandler : IHandler<Envelope<Order>> { }
                public class OrderHandler : IHandler<Order> { }
            }
            """ + Startup("ByBaseType<IHandler<Envelope<Order>>>(Lifetime.Scoped)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IHandler<global::TestAssembly.Envelope<global::TestAssembly.Order>>, global::TestAssembly.EnvelopeHandler>();", generated);
        Assert.DoesNotContain("global::TestAssembly.OrderHandler", generated);
    }

    [Fact]
    public void ByBaseType_ClosedGenericViaTypeof_Registers()
    {
        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public interface IRepository<T> { }
                public class OrderRepository : IRepository<Order> { }
            }
            """ + Startup("ByBaseType(typeof(IRepository<Order>), Lifetime.Scoped)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();", generated);
    }

    [Fact]
    public void ByBaseType_NestedInterface_Registers()
    {
        var source = """
            namespace TestAssembly
            {
                public static class Outer
                {
                    public interface INestedHandler { }
                    public class InnerHandler : INestedHandler { }
                }

                public class TopLevelHandler : Outer.INestedHandler { }
            }
            """ + Startup("ByBaseType<Outer.INestedHandler>(Lifetime.Scoped)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("baseType == typeof(global::TestAssembly.Outer.INestedHandler)", generated);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Outer.INestedHandler, global::TestAssembly.TopLevelHandler>();", generated);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Outer.INestedHandler, global::TestAssembly.Outer.InnerHandler>();", generated);
    }

    [Fact]
    public void ByBaseType_DoublyNestedClass_Registers()
    {
        var source = """
            namespace TestAssembly
            {
                public class Level1
                {
                    public class Level2
                    {
                        public abstract class JobBase { }
                    }
                }

                public class NightlyJob : Level1.Level2.JobBase { }
            }
            """ + Startup("ByBaseType<Level1.Level2.JobBase>(Lifetime.Singleton, ServiceTypeSelector.Self)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("baseType == typeof(global::TestAssembly.Level1.Level2.JobBase)", generated);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.NightlyJob>();", generated);
    }

    [Fact]
    public void ByBaseType_InterfaceNestedInClosedGeneric_Registers()
    {
        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public class Invoice { }
                public class Container<T>
                {
                    public interface IInner { }
                }

                public class OrderInner : Container<Order>.IInner { }
                public class InvoiceInner : Container<Invoice>.IInner { }
            }
            """ + Startup("ByBaseType<Container<Order>.IInner>(Lifetime.Scoped)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Container<global::TestAssembly.Order>.IInner, global::TestAssembly.OrderInner>();", generated);
        Assert.DoesNotContain("InvoiceInner", generated);
    }

    [Fact]
    public void ByBaseType_OpenGenericNestedInterface_RegistersClosedAndOpenForms()
    {
        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public static class Outer
                {
                    public interface IRepository<T> { }
                }

                public class Repository<T> : Outer.IRepository<T> { }
                public class OrderRepository : Outer.IRepository<Order> { }
            }
            """ + Startup("ByBaseType(typeof(Outer.IRepository<>), Lifetime.Scoped)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("baseType == typeof(global::TestAssembly.Outer.IRepository<>)", generated);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Outer.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();", generated);
        Assert.Contains("Services.AddScoped(typeof(global::TestAssembly.Outer.IRepository<>), typeof(global::TestAssembly.Repository<>));", generated);
    }

    [Fact]
    public void ByBaseType_OpenGenericWithTwoTypeParameters_RegistersClosedAndOpenForms()
    {
        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public class OrderDto { }
                public interface IMapper<TSource, TTarget> { }
                public class Mapper<TSource, TTarget> : IMapper<TSource, TTarget> { }
                public class OrderMapper : IMapper<Order, OrderDto> { }
            }
            """ + Startup("ByBaseType(typeof(IMapper<,>), Lifetime.Transient)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("baseType == typeof(global::TestAssembly.IMapper<,>)", generated);
        Assert.Contains("Services.AddTransient<global::TestAssembly.IMapper<global::TestAssembly.Order, global::TestAssembly.OrderDto>, global::TestAssembly.OrderMapper>();", generated);
        Assert.Contains("Services.AddTransient(typeof(global::TestAssembly.IMapper<,>), typeof(global::TestAssembly.Mapper<,>));", generated);
    }

    [Fact]
    public void ByBaseType_OpenAndClosedFormsOfSameGeneric_AreDistinctRules()
    {
        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public class Invoice { }
                public interface IRepository<T> { }
                public class OrderRepository : IRepository<Order> { }
                public class InvoiceRepository : IRepository<Invoice> { }
            }
            """ + Startup(
                "ByBaseType(typeof(IRepository<>), Lifetime.Singleton)",
                "ByBaseType<IRepository<Order>>(Lifetime.Scoped)");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("baseType == typeof(global::TestAssembly.IRepository<>) && lifetime == Lifetime.Singleton", generated);
        Assert.Contains("baseType == typeof(global::TestAssembly.IRepository<global::TestAssembly.Order>) && lifetime == Lifetime.Scoped", generated);
        Assert.Contains("Services.AddSingleton<global::TestAssembly.IRepository<global::TestAssembly.Invoice>, global::TestAssembly.InvoiceRepository>();", generated);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();", generated);
    }

    [Fact]
    public void ByBaseType_NestedAttributeFilter_OnlyMatchesDecoratedTypes()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IService { }

                public static class Markers
                {
                    [System.AttributeUsage(System.AttributeTargets.Class)]
                    public sealed class AutoRegisterAttribute : System.Attribute { }
                }

                [Markers.AutoRegister]
                public class Included : IService { }

                public class Excluded : IService { }
            }
            """ + Startup("ByBaseType<IService>(Lifetime.Scoped, ServiceTypeSelector.BaseType, typeof(Markers.AutoRegisterAttribute))");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("matchAttribute == typeof(global::TestAssembly.Markers.AutoRegisterAttribute)", generated);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IService, global::TestAssembly.Included>();", generated);
        Assert.DoesNotContain("Excluded", generated);
    }

    [Fact]
    public void ByBaseType_FrameworkAttributeFilter_OnlyMatchesDecoratedTypes()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IService { }

                [System.CLSCompliant(true)]
                public class Included : IService { }

                public class Excluded : IService { }
            }
            """ + Startup("ByBaseType<IService>(Lifetime.Transient, matchAttribute: typeof(System.CLSCompliantAttribute))");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("matchAttribute == typeof(global::System.CLSCompliantAttribute)", generated);
        Assert.Contains("Services.AddTransient<global::TestAssembly.IService, global::TestAssembly.Included>();", generated);
        Assert.DoesNotContain("Excluded", generated);
    }

    [Fact]
    public void ByBaseType_OpenGenericWithAttributeFilter_OnlyMatchesDecoratedTypes()
    {
        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public class Invoice { }
                public interface IRepository<T> { }

                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class AutoRegisterAttribute : System.Attribute { }

                [AutoRegister]
                public class OrderRepository : IRepository<Order> { }

                public class InvoiceRepository : IRepository<Invoice> { }
            }
            """ + Startup("ByBaseType(typeof(IRepository<>), Lifetime.Scoped, ServiceTypeSelector.BaseType, typeof(AutoRegisterAttribute))");

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();", generated);
        Assert.DoesNotContain("InvoiceRepository", generated);
    }

    [Fact]
    public void ByBaseType_BaseTypeFromProjectReference_Registers()
    {
        const string referenced = """
            namespace Shared.Contracts
            {
                public interface IPlugin { }
                public interface IRepository<T> { }
            }
            """;

        var source = """
            namespace TestAssembly
            {
                public class Order { }
                public class MyPlugin : Shared.Contracts.IPlugin { }
                public class OrderRepository : Shared.Contracts.IRepository<Order> { }
            }
            """ + Startup(
                "ByBaseType<Shared.Contracts.IPlugin>(Lifetime.Scoped)",
                "ByBaseType<Shared.Contracts.IRepository<Order>>(Lifetime.Transient)");

        var (output, diagnostics) = CompilationHelper.RunGeneratorWithProjectReference(source, referenced);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("Services.AddScoped<global::Shared.Contracts.IPlugin, global::TestAssembly.MyPlugin>();", generated);
        Assert.Contains("Services.AddTransient<global::Shared.Contracts.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();", generated);
    }

    [Fact]
    public void ByBaseType_BaseTypeDeclaredInSourceAndInReference_UsesTheOneTheCallSiteBindsTo()
    {
        const string referenced = """
            namespace TestAssembly
            {
                public interface IService { }
            }
            """;

        var source = """
            namespace TestAssembly
            {
                public interface IService { }
                public class SourceService : IService { }
            }
            """ + Startup("ByBaseType<IService>(Lifetime.Scoped)");

        var (output, diagnostics) = CompilationHelper.RunGeneratorWithProjectReference(source, referenced);

        Assert.Empty(diagnostics);
        var generated = Generated(output, ByBaseTypeHint);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IService, global::TestAssembly.SourceService>();", generated);
    }

    [Fact]
    public void ByNamespaceOf_NestedGenericMarker_RestrictsToItsNamespace()
    {
        var source = """
            namespace MyApp.Data
            {
                public static class Markers
                {
                    public class Marker<T> { }
                }

                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MyApp.Other
            {
                public interface IBar { }
                public class Bar : IBar { }
            }

            namespace MyApp
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespaceOf<MyApp.Data.Markers.Marker<int>>(ns => true, Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, "ServicefyConventionsBuilder.ByNamespaceOf.g.cs");
        Assert.Contains("Services.AddScoped<global::MyApp.Data.IFoo, global::MyApp.Data.Foo>();", generated);
        Assert.DoesNotContain("global::MyApp.Other", generated);
    }

    [Fact]
    public void ByNamespace_MultiLinePredicateWithComments_IsReParsedAndMatches()
    {
        var source = """
            namespace MyApp.Data
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MyApp.Services
            {
                public interface IBar { }
                public class Bar : IBar { }
            }

            namespace MyApp.Web
            {
                public interface IBaz { }
                public class Baz : IBaz { }
            }

            namespace MyApp
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(
                            ns => ns.EndsWith(".Data") // data access
                                  || /* application services */ ns.EndsWith(".Services"),
                            Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, "ServicefyConventionsBuilder.ByNamespace.g.cs");
        Assert.Contains("Services.AddScoped<global::MyApp.Data.IFoo, global::MyApp.Data.Foo>();", generated);
        Assert.Contains("Services.AddScoped<global::MyApp.Services.IBar, global::MyApp.Services.Bar>();", generated);
        Assert.DoesNotContain("Baz", generated);
    }

    [Fact]
    public void ByNamespace_PredicateWithEscapedAndVerbatimLiterals_IsReParsedAndMatches()
    {
        var source = """
            namespace MyApp.Data
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MyApp.Web
            {
                public interface IBaz { }
                public class Baz : IBaz { }
            }

            namespace MyApp
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("MyApp") && ns.EndsWith(@"Data"), Lifetime.Singleton);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, "ServicefyConventionsBuilder.ByNamespace.g.cs");
        Assert.Contains("Services.AddSingleton<global::MyApp.Data.IFoo, global::MyApp.Data.Foo>();", generated);
        Assert.DoesNotContain("Baz", generated);
    }

    [Fact]
    public void ByTypeName_PredicateWithStringComparisonArrayAndNegation_IsReParsedAndMatches()
    {
        var source = """
            namespace MyApp.Data
            {
                public interface IOrderRepository { }
                public class OrderRepository : IOrderRepository { }

                public interface IOrderQueryHandler { }
                public class OrderQueryHANDLER : IOrderQueryHandler { }

                public interface IFake { }
                public class FakeRepository : IFake { }
            }

            namespace MyApp
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByTypeName(
                            (ns, name) => name.EndsWith(["repository", "handler"], StringComparison.OrdinalIgnoreCase)
                                          && !name.StartsWith("Fake"),
                            Lifetime.Transient);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, "ServicefyConventionsBuilder.ByTypeName.g.cs");
        Assert.Contains("Services.AddTransient<global::MyApp.Data.IOrderRepository, global::MyApp.Data.OrderRepository>();", generated);
        Assert.Contains("Services.AddTransient<global::MyApp.Data.IOrderQueryHandler, global::MyApp.Data.OrderQueryHANDLER>();", generated);
        Assert.DoesNotContain("FakeRepository", generated);
    }

    [Fact]
    public void ByNamespace_SamePredicateTextAtTwoCallSites_EmitsOneBranch()
    {
        var source = """
            namespace MyApp.Data
            {
                public interface IFoo { }
                public class Foo : IFoo { }
            }

            namespace MyApp
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.EndsWith("Data"), Lifetime.Scoped);
                    }

                    public void ConfigureAgain(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespace(ns => ns.EndsWith("Data"), Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = Generated(output, "ServicefyConventionsBuilder.ByNamespace.g.cs");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(generated, "if \\(lifetime == Lifetime.Scoped"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(generated, "Services.AddScoped<global::MyApp.Data.IFoo, global::MyApp.Data.Foo>\\(\\);"));
    }

    [Fact]
    public void ByBaseType_SelfWithInterface_SVCFY010_PointsAtTheTypeArgument()
    {
        var source = """
            namespace TestAssembly
            {
                public static class Outer { public interface INested { } }
                public class Impl : Outer.INested { }
            }
            """ + Startup("ByBaseType<Outer.INested>(Lifetime.Scoped, ServiceTypeSelector.Self)");

        var (_, diagnostics) = CompilationHelper.RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "SVCFY010");
        Assert.Contains("'TestAssembly.Outer.INested' is an interface", diagnostic.GetMessage());

        var expectedStart = source.IndexOf("ByBaseType<Outer.INested>", StringComparison.Ordinal) + "ByBaseType<".Length;
        Assert.Equal(expectedStart, diagnostic.Location.SourceSpan.Start);
        Assert.Equal("Outer.INested".Length, diagnostic.Location.SourceSpan.Length);

        var line = source[..expectedStart].Count(c => c == '\n');
        Assert.Equal(line, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }
}
