using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class SharedTypePoolGenerationTests
{
    private const string ReferencedWidgets = """
        namespace Ref.Widgets
        {
            public interface IRefWidget { }
            public interface IDerivedRefWidget : IRefWidget { }

            public class RefWidget : IRefWidget { }

            public abstract class AbstractRefWidget : IRefWidget { }
            public static class StaticRefWidget { }
            public struct StructRefWidget : IRefWidget { }
            public class OpenRefWidget<T> : IRefWidget { }
            public class NoInterfaceRefWidget { }
            internal class InternalRefWidget : IRefWidget { }

            public class RefWidgetHolder
            {
                internal class HiddenRefWidget : IRefWidget { }
            }

            public class DecoratingRefWidget : IRefWidget
            {
                public DecoratingRefWidget(IRefWidget inner) { }
            }
        }
        """;

    private const string LocalWidgets = """
        namespace TestAssembly.Widgets
        {
            public class Marker { }

            public interface ILocalWidget { }
            public interface IDerivedLocalWidget : ILocalWidget { }

            public class LocalWidget : ILocalWidget { }

            public abstract class AbstractLocalWidget : ILocalWidget { }
            public static class StaticLocalWidget { }
            public struct StructLocalWidget : ILocalWidget { }
            public class OpenLocalWidget<T> : ILocalWidget { }
            public class NoInterfaceLocalWidget { }

            [AddScoped]
            public class AttributedLocalWidget : ILocalWidget { }

            [Decorator]
            public class DecoratingLocalWidget : ILocalWidget
            {
                public DecoratingLocalWidget(ILocalWidget inner) { }
            }
        }

        namespace TestAssembly
        {
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.Decorate<Ref.Widgets.IRefWidget, Ref.Widgets.DecoratingRefWidget>();
                    services.AddServicefyConventions().ByNamespace(ns => ns.EndsWith("Widgets"), Lifetime.Scoped);
                    services.AddServicefyConventions().ByTypeName((ns, name) => name.Contains("Widget"), Lifetime.Transient);
                    services.AddServicefyConventions().ByNamespaceOf<TestAssembly.Widgets.Marker>(ns => true, Lifetime.Singleton);
                    services.AddServicefyConventions().ByBaseType<TestAssembly.Widgets.ILocalWidget>(Lifetime.Scoped);
                    services.AddServicefyConventions().ByBaseType<Ref.Widgets.IRefWidget>(Lifetime.Scoped);
                }
            }
        }
        """;

    private static readonly string[] LocalDecoys =
    [
        "AbstractLocalWidget", "StaticLocalWidget", "StructLocalWidget", "OpenLocalWidget",
        "NoInterfaceLocalWidget", "AttributedLocalWidget", "IDerivedLocalWidget",
    ];

    private static readonly string[] RefDecoys =
    [
        "AbstractRefWidget", "StaticRefWidget", "StructRefWidget", "OpenRefWidget",
        "NoInterfaceRefWidget", "InternalRefWidget", "HiddenRefWidget", "IDerivedRefWidget",
    ];

    [Theory]
    [InlineData("ServicefyConventionsBuilder.ByNamespace.g.cs", true, true)]
    [InlineData("ServicefyConventionsBuilder.ByTypeName.g.cs", true, true)]
    [InlineData("ServicefyConventionsBuilder.ByNamespaceOf.g.cs", true, false)]
    [InlineData("ServicefyConventionsBuilder.ByBaseType.g.cs", true, true)]
    public void Conventions_RegisterOnlyEligibleTypes_FromSourceAndProjectReference(string hint, bool local, bool referenced)
    {
        var (output, diagnostics) = CompilationHelper.RunGeneratorWithProjectReference(LocalWidgets, ReferencedWidgets, "Widgets");

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("SVCFY", StringComparison.Ordinal));

        var generated = output.SyntaxTrees.FirstOrDefault(t => t.FilePath.EndsWith(hint, StringComparison.Ordinal))?.ToString();
        Assert.NotNull(generated);

        var implementations = RegisteredImplementations(generated);
        var expected = new List<string>();
        if (local) expected.Add("global::TestAssembly.Widgets.LocalWidget");
        if (referenced) expected.Add("global::Ref.Widgets.RefWidget");

        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), implementations.OrderBy(x => x, StringComparer.Ordinal));

        foreach (var decoy in LocalDecoys.Concat(RefDecoys))
            Assert.DoesNotContain(decoy, generated);

        if (local)
            Assert.Equal(["TestAssembly.Widgets.DecoratingLocalWidget"],
                GeneratorOutputAssert.DecoratorChain(generated, "global::TestAssembly.Widgets.ILocalWidget"));
        if (referenced)
            Assert.Equal(["Ref.Widgets.DecoratingRefWidget"],
                GeneratorOutputAssert.DecoratorChain(generated, "global::Ref.Widgets.IRefWidget"));
    }

    private static List<string> RegisteredImplementations(string generated) =>
        Regex.Matches(generated, @"Services\.Add(?:Keyed)?(?:Scoped|Singleton|Transient)<(?<svc>global::[\w.]+), (?<impl>global::[\w.]+)>\(")
            .Select(m => m.Groups["impl"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void ClosedGenericDecorate_TakesPrecedenceOverOpenGenericChain_ForThatClosedServiceOnly()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IRepository<T> { }
                public class Cliente { }
                public class Pedido { }
                public class ClienteRepository : IRepository<Cliente> { }
                public class PedidoRepository : IRepository<Pedido> { }

                public class LoggingRepository<T> : IRepository<T>
                {
                    public LoggingRepository(IRepository<T> inner) { }
                }

                public class AuditClienteRepository : IRepository<Cliente>
                {
                    public AuditClienteRepository(IRepository<Cliente> inner) { }
                }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions()
                            .ByBaseType(typeof(IRepository<>), Lifetime.Scoped)
                            .Decorate(typeof(IRepository<>), typeof(LoggingRepository<>))
                            .Decorate<IRepository<Cliente>, AuditClienteRepository>();
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs", StringComparison.Ordinal))?.ToString();
        Assert.NotNull(generated);

        Assert.Equal(
            ["TestAssembly.AuditClienteRepository"],
            GeneratorOutputAssert.DecoratorChain(generated, "global::TestAssembly.IRepository<global::TestAssembly.Cliente>"));
        Assert.Equal(
            ["TestAssembly.LoggingRepository<global::TestAssembly.Pedido>"],
            GeneratorOutputAssert.DecoratorChain(generated, "global::TestAssembly.IRepository<global::TestAssembly.Pedido>"));

        Assert.DoesNotContain(", global::TestAssembly.AuditClienteRepository>", generated);
    }

    [Fact]
    public void DecorateWithNestedTypes_ResolvesServiceAndDecorator()
    {
        var source = """
            namespace TestAssembly
            {
                public static class Billing
                {
                    public interface IInvoicer { }

                    [AddScoped]
                    public class Invoicer : IInvoicer { }

                    public class LoggingInvoicer : IInvoicer
                    {
                        public LoggingInvoicer(IInvoicer inner) { }
                    }
                }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.Decorate<Billing.IInvoicer, Billing.LoggingInvoicer>();
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        Assert.Empty(diagnostics);
        var generated = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("class ServicefyExtensions", StringComparison.Ordinal));
        Assert.NotNull(generated);

        Assert.Equal(
            ["TestAssembly.Billing.LoggingInvoicer"],
            GeneratorOutputAssert.DecoratorChain(generated, "global::TestAssembly.Billing.IInvoicer"));
    }

    [Fact]
    public void DecorateInSeveralFiles_ChainOrderedByFileThenPosition()
    {
        const string types = """
            namespace TestAssembly
            {
                public interface IClock { }

                [AddSingleton]
                public class Clock : IClock { }

                public class FirstClockDecorator : IClock { public FirstClockDecorator(IClock inner) { } }
                public class SecondClockDecorator : IClock { public SecondClockDecorator(IClock inner) { } }
                public class ThirdClockDecorator : IClock { public ThirdClockDecorator(IClock inner) { } }
            }
            """;
        const string a = """
            namespace TestAssembly
            {
                public static class A
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.Decorate<IClock, FirstClockDecorator>();
                        services.Decorate<IClock, SecondClockDecorator>();
                    }
                }
            }
            """;
        const string b = """
            namespace TestAssembly
            {
                public static class B
                {
                    public static void Configure(IServiceCollection services) => services.Decorate<IClock, ThirdClockDecorator>();
                }
            }
            """;

        foreach (var order in new[] { new[] { ("A.cs", a), ("B.cs", b) }, new[] { ("B.cs", b), ("A.cs", a) } })
        {
            var run = IncrementalGeneratorHelper.RunWithEdit(order.Prepend(("Types.cs", types)), CompilationEdits.None);

            Assert.Empty(run.Initial.Diagnostics);
            Assert.Equal(
                ["TestAssembly.FirstClockDecorator", "TestAssembly.SecondClockDecorator", "TestAssembly.ThirdClockDecorator"],
                GeneratorOutputAssert.DecoratorChain(
                    run.InitialGeneratedSource<Package.ByConfiguration.Generators.ServicefyByConfigurationGenerator>("ServicefyExtensions.TestAssembly.g.cs")!,
                    "global::TestAssembly.IClock"));
        }
    }

    [Fact]
    public void DecorateWithUnresolvedServiceType_DecoratorIsStillNotRegisteredByConvention()
    {
        var source = """
            namespace TestAssembly.Clocks
            {
                public interface IClock { }
                public class Clock : IClock { }

                public class LoggingClock : IClock
                {
                    public LoggingClock(IClock inner) { }
                }
            }

            namespace TestAssembly
            {
                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.Decorate<TestAssembly.Clocks.IClok, TestAssembly.Clocks.LoggingClock>();
                        services.AddServicefyConventions().ByNamespace(ns => ns == "TestAssembly.Clocks", Lifetime.Scoped);
                    }
                }
            }
            """;

        var (output, _) = CompilationHelper.RunGenerator(source);

        var generated = output.SyntaxTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespace.g.cs", StringComparison.Ordinal))?.ToString();
        Assert.NotNull(generated);
        Assert.Contains("Services.AddScoped<global::TestAssembly.Clocks.IClock, global::TestAssembly.Clocks.Clock>();", generated);
        Assert.DoesNotContain("global::TestAssembly.Clocks.LoggingClock>", generated);
    }

    [Fact]
    public void DecorateWithUnresolvedDecoratorType_StillReportsSvcfy013AtTheCall()
    {
        var source = """
            namespace TestAssembly
            {
                public interface IClock { }

                [AddScoped]
                public class Clock : IClock { }

                public class Startup
                {
                    public void Configure(IServiceCollection services)
                    {
                        services.Decorate<IClock, LoggingClok>();
                    }
                }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source);

        var generated = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("class ServicefyExtensions", StringComparison.Ordinal));
        Assert.NotNull(generated);
        Assert.Contains("AddScoped<global::TestAssembly.IClock, global::TestAssembly.Clock>()", generated);

        var svcfy013 = Assert.Single(diagnostics, d => d.Id == "SVCFY013");
        Assert.Equal("services.Decorate<IClock, LoggingClok>()", svcfy013.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(svcfy013.Location.SourceSpan));
    }
}
