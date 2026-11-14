using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class ByBaseTypeNuGetAnchorTests
{
    private const string ContractsName = "MyCompany.Contracts";
    private const string Ns = "global::MyCompany.Contracts";

    private static ReferencedAssembly Contracts(string body, string usings = "") =>
        ReferencedAssembly.Package(ContractsName, $$"""
            {{usings}}
            namespace MyCompany.Contracts
            {
            {{body}}
            }
            """);

    private static string Consumer(string types, string rules, string usings = "using MyCompany.Contracts;") => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        {{usings}}

        namespace TestAssembly
        {
        {{types}}

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddServicefyConventions()
                        {{rules}};
                }
            }
        }
        """;

    private static void AssertCleanBuild(PackageRunResult result)
    {
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationErrors);
    }

    [Fact]
    public void Acceptance_ImplementedInterfaces_RegistersConsumerImplementationAgainstPackageInterface()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
            """);

        var source = Consumer(
            types: """
                    public class Order { }
                    public class OrderRepository : MyCompany.Contracts.IRepository<Order> { }
                """,
            rules: ".ByBaseType<MyCompany.Contracts.IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)",
            usings: "");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.NotNull(result.ByBaseTypeSource);
        Assert.Contains(
            $"if (baseType == typeof({Ns}.IRepository<global::TestAssembly.Order>) && lifetime == Lifetime.Scoped && selector == ServiceTypeSelector.ImplementedInterfaces && matchAttribute == null)",
            result.ByBaseTypeSource);
        Assert.Contains(
            $"Services.AddScoped<{Ns}.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();",
            result.ByBaseTypeSource);
    }

    [Fact]
    public void Acceptance_ImplementedInterfaces_ResolvesConsumerImplementationAtRuntime()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
            """);

        var source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace TestAssembly
            {
                public class Order { }
                public class OrderRepository : MyCompany.Contracts.IRepository<Order> { }

                public static class Acceptance
                {
                    public static string Resolve()
                    {
                        var services = new ServiceCollection();
                        services.AddServicefyConventions()
                            .ByBaseType<MyCompany.Contracts.IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces);

                        using var provider = services.BuildServiceProvider();
                        using var scope = provider.CreateScope();
                        return scope.ServiceProvider.GetRequiredService<MyCompany.Contracts.IRepository<Order>>().GetType().FullName;
                    }
                }
            }
            """;

        var resolved = PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Acceptance", "Resolve", contracts);

        Assert.Equal("TestAssembly.OrderRepository", resolved);
    }

    [Fact]
    public void DefaultBaseTypeSelector_PackageInterface_RegistersConsumerImplementation()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
            """);

        var source = Consumer(
            types: """
                    public class Order { }
                    public class OrderRepository : IRepository<Order> { }
                """,
            rules: ".ByBaseType<IRepository<Order>>(Lifetime.Transient)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Contains(
            $"Services.AddTransient<{Ns}.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();",
            result.ByBaseTypeSource);
    }

    [Fact]
    public void PackageInterface_NoImplementationAnywhere_EmitsNoByBaseTypeFile()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
            """);

        var source = Consumer(
            types: "    public class Order { }",
            rules: ".ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Null(result.ByBaseTypeSource);
    }

    [Fact]
    public void ImplementationShippedInPackage_IsRegisteredAlongsideConsumerImplementations()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : IPlugin { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Singleton)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Contains($"Services.AddSingleton<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", result.ByBaseTypeSource);
        Assert.Contains($"Services.AddSingleton<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", result.ByBaseTypeSource);
    }

    [Fact]
    public void ImplementationShippedInPackage_ClosedGenericTBase_IsRegisteredAgainstPackageInterface()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
                public class Order { }
                public class Customer { }
                public class InMemoryOrderRepository : IRepository<Order> { }
                public class InMemoryCustomerRepository : IRepository<Customer> { }
            """);

        var source = Consumer(
            types: "    public class SqlOrderRepository : IRepository<Order> { }",
            rules: ".ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Contains($"Services.AddScoped<{Ns}.IRepository<{Ns}.Order>, {Ns}.InMemoryOrderRepository>();", result.ByBaseTypeSource);
        Assert.Contains($"Services.AddScoped<{Ns}.IRepository<{Ns}.Order>, global::TestAssembly.SqlOrderRepository>();", result.ByBaseTypeSource);
        Assert.DoesNotContain("InMemoryCustomerRepository", result.ByBaseTypeSource);
    }

    [Fact]
    public void ImplementationShippedInPackage_ResolvesAtRuntime()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestAssembly
            {
                public class ExportPlugin : MyCompany.Contracts.IPlugin { }

                public static class Plugins
                {
                    public static string Resolve()
                    {
                        var services = new ServiceCollection();
                        services.AddServicefyConventions().ByBaseType<MyCompany.Contracts.IPlugin>(Lifetime.Singleton);

                        using var provider = services.BuildServiceProvider();
                        var names = new List<string>();
                        foreach (var plugin in provider.GetServices<MyCompany.Contracts.IPlugin>())
                            names.Add(plugin.GetType().FullName);

                        names.Sort(System.StringComparer.Ordinal);
                        return string.Join(",", names);
                    }
                }
            }
            """;

        var resolved = PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Plugins", "Resolve", contracts);

        Assert.Equal("MyCompany.Contracts.BuiltInPlugin,TestAssembly.ExportPlugin", resolved);
    }

    [Fact]
    public void PackageCandidates_OnlyPublicConcreteNonGenericClasses_AreRegistered()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public interface IAdvancedPlugin : IPlugin { }

                public class VisiblePlugin : IPlugin { }
                internal class HiddenPlugin : IPlugin { }
                public abstract class AbstractPlugin : IPlugin { }
                public class DerivedFromAbstractPlugin : AbstractPlugin { }
                public class OpenGenericPlugin<T> : IPlugin { }
                public struct ValueTypePlugin : IPlugin { }
                public record RecordPlugin : IPlugin;

                public class PublicOuter
                {
                    public class PublicNestedPlugin : IPlugin { }
                    internal class InternalNestedPlugin : IPlugin { }
                    protected class ProtectedNestedPlugin : IPlugin { }
                    private class PrivateNestedPlugin : IPlugin { }
                }

                internal class InternalOuter
                {
                    public class NestedInInternalOuterPlugin : IPlugin { }
                }
            """);

        var source = Consumer(types: "", rules: ".ByBaseType<IPlugin>(Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.NotNull(generated);

        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.VisiblePlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.DerivedFromAbstractPlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.RecordPlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.PublicOuter.PublicNestedPlugin>();", generated);

        Assert.DoesNotContain("HiddenPlugin", generated);
        Assert.DoesNotContain($"{Ns}.AbstractPlugin>", generated);
        Assert.DoesNotContain("OpenGenericPlugin", generated);
        Assert.DoesNotContain("ValueTypePlugin", generated);
        Assert.DoesNotContain("IAdvancedPlugin>", generated);
        Assert.DoesNotContain("InternalNestedPlugin", generated);
        Assert.DoesNotContain("ProtectedNestedPlugin", generated);
        Assert.DoesNotContain("PrivateNestedPlugin", generated);
        Assert.DoesNotContain("NestedInInternalOuterPlugin", generated);
    }

    [Fact]
    public void ImplementedInterfaces_RegistersEveryDirectPackageInterface_ButNotBclInterfaces()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public interface IConfigurable { }
            """);

        var source = Consumer(
            types: """
                    public interface ILocalService { }

                    public class ExportPlugin : IPlugin, IConfigurable, ILocalService, IDisposable
                    {
                        public void Dispose() { }
                    }
                """,
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IConfigurable, global::TestAssembly.ExportPlugin>();", generated);
        Assert.Contains("Services.AddScoped<global::TestAssembly.ILocalService, global::TestAssembly.ExportPlugin>();", generated);
        Assert.DoesNotContain("IDisposable", generated);
    }

    [Fact]
    public void ImplementedInterfaces_InheritedPackageInterface_IsNotRegistered()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public interface IAdvancedPlugin : IPlugin { }
            """);

        var source = Consumer(
            types: "    public class TurboPlugin : IAdvancedPlugin { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Contains($"Services.AddScoped<{Ns}.IAdvancedPlugin, global::TestAssembly.TurboPlugin>();", result.ByBaseTypeSource);
        Assert.DoesNotContain($"Services.AddScoped<{Ns}.IPlugin,", result.ByBaseTypeSource);
    }

    [Fact]
    public void AllImplementedInterfaces_RegistersInheritedAndClosedGenericPackageInterfaces()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
                public interface IReadOnlyRepository { }
                public abstract class RepositoryBase<T> : IRepository<T>, IReadOnlyRepository { }
                public interface IAuditable { }
            """);

        var source = Consumer(
            types: """
                    public class Order { }
                    public class OrderRepository : RepositoryBase<Order>, IAuditable, IDisposable
                    {
                        public void Dispose() { }
                    }
                """,
            rules: ".ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.AllImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddScoped<{Ns}.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IReadOnlyRepository, global::TestAssembly.OrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IAuditable, global::TestAssembly.OrderRepository>();", generated);
        Assert.DoesNotContain("IDisposable", generated);
        Assert.DoesNotContain("RepositoryBase", generated);
    }

    [Fact]
    public void SelfWithInterfaces_ForwardsPackageInterfaces_ForConsumerAndPackageImplementations()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public interface IConfigurable { }
                public class BuiltInPlugin : IPlugin, IConfigurable, System.IDisposable
                {
                    public void Dispose() { }
                }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : IPlugin, IConfigurable { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Singleton, ServiceTypeSelector.SelfWithInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;

        Assert.Contains("Services.AddSingleton<global::TestAssembly.ExportPlugin>();", generated);
        Assert.Contains($"Services.AddSingleton<{Ns}.IPlugin>(sp => sp.GetRequiredService<global::TestAssembly.ExportPlugin>());", generated);
        Assert.Contains($"Services.AddSingleton<{Ns}.IConfigurable>(sp => sp.GetRequiredService<global::TestAssembly.ExportPlugin>());", generated);

        Assert.Contains($"Services.AddSingleton<{Ns}.BuiltInPlugin>();", generated);
        Assert.Contains($"Services.AddSingleton<{Ns}.IPlugin>(sp => sp.GetRequiredService<{Ns}.BuiltInPlugin>());", generated);
        Assert.Contains($"Services.AddSingleton<{Ns}.IConfigurable>(sp => sp.GetRequiredService<{Ns}.BuiltInPlugin>());", generated);

        Assert.DoesNotContain("IDisposable", generated);
    }

    [Fact]
    public void Self_PackageBaseClass_RegistersConsumerAndPackageDerivedClasses()
    {
        var contracts = Contracts("""
                public abstract class JobBase { }
                public class HeartbeatJob : JobBase { }
                public class Unrelated { }
            """);

        var source = Consumer(
            types: "    public class SendDigestJob : JobBase { }",
            rules: ".ByBaseType<JobBase>(Lifetime.Singleton, ServiceTypeSelector.Self)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains("Services.AddSingleton<global::TestAssembly.SendDigestJob>();", generated);
        Assert.Contains($"Services.AddSingleton<{Ns}.HeartbeatJob>();", generated);
        Assert.DoesNotContain("Unrelated", generated);
        Assert.DoesNotContain($"AddSingleton<{Ns}.JobBase>", generated);
    }

    [Fact]
    public void BaseType_PackageBaseClass_RegistersDerivedClassesAgainstPackageBaseClass()
    {
        var contracts = Contracts("""
                public abstract class JobBase { }
                public class HeartbeatJob : JobBase { }
                public class NightlyHeartbeatJob : HeartbeatJob { }
            """);

        var source = Consumer(
            types: "    public class SendDigestJob : JobBase { }",
            rules: ".ByBaseType<JobBase>(Lifetime.Singleton)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddSingleton<{Ns}.JobBase, global::TestAssembly.SendDigestJob>();", generated);
        Assert.Contains($"Services.AddSingleton<{Ns}.JobBase, {Ns}.HeartbeatJob>();", generated);
        Assert.Contains($"Services.AddSingleton<{Ns}.JobBase, {Ns}.NightlyHeartbeatJob>();", generated);
    }

    [Fact]
    public void Self_PackageInterface_StillReportsSVCFY010()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : IPlugin { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Singleton, ServiceTypeSelector.Self)");

        var result = PackageCompilationHelper.Run(source, contracts);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "SVCFY010");
        Assert.Null(result.Branch($"{Ns}.IPlugin"));
    }

    [Fact]
    public void MatchAttribute_DeclaredInConsumer_ExcludesUnattributedPackageImplementations()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var source = Consumer(
            types: """
                    [AttributeUsage(AttributeTargets.Class)]
                    public sealed class ExportedAttribute : Attribute { }

                    [Exported]
                    public class ExportPlugin : IPlugin { }

                    public class DraftPlugin : IPlugin { }
                """,
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped, matchAttribute: typeof(ExportedAttribute))");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains("matchAttribute == typeof(global::TestAssembly.ExportedAttribute)", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", generated);
        Assert.DoesNotContain("BuiltInPlugin", generated);
        Assert.DoesNotContain("DraftPlugin", generated);
    }

    [Fact]
    public void MatchAttribute_DeclaredInPackage_MatchesDirectlyAttributedPackageAndConsumerImplementations()
    {
        var contracts = Contracts(
            usings: "using System;",
            body: """
                [AttributeUsage(AttributeTargets.Class, Inherited = true)]
                public sealed class PluginExportAttribute : Attribute { }

                public interface IPlugin { }

                [PluginExport]
                public class BuiltInPlugin : IPlugin { }

                public class InternalToolPlugin : IPlugin { }

                // Inherits the attribute only through its base class: matchAttribute needs it directly.
                public class DerivedBuiltInPlugin : BuiltInPlugin { }
            """);

        var source = Consumer(
            types: """
                    [PluginExport]
                    public class ExportPlugin : IPlugin { }

                    public class DraftPlugin : IPlugin { }
                """,
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped, matchAttribute: typeof(PluginExportAttribute))");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"matchAttribute == typeof({Ns}.PluginExportAttribute)", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", generated);
        Assert.DoesNotContain("InternalToolPlugin", generated);
        Assert.DoesNotContain("DerivedBuiltInPlugin", generated);
        Assert.DoesNotContain("DraftPlugin", generated);
    }

    [Fact]
    public void Decorator_ConsumerDecoratorForPackageInterface_WrapsSingleImplementation()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
            """);

        var source = Consumer(
            types: """
                    public class Order { }
                    public class OrderRepository : IRepository<Order> { }

                    [DecoratorFor<IRepository<Order>>]
                    public class LoggingOrderRepository : IRepository<Order>
                    {
                        public LoggingOrderRepository(IRepository<Order> inner) { }
                    }
                """,
            rules: ".ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        var service = $"{Ns}.IRepository<global::TestAssembly.Order>";
        Assert.Contains($"Services.AddKeyedScoped<{service}, global::TestAssembly.OrderRepository>(\"__BASE__\");", generated);
        Assert.Contains("new global::TestAssembly.LoggingOrderRepository(", generated);
        Assert.Contains($"Services.AddScoped<{service}>(sp =>", generated);
        Assert.DoesNotContain($"Services.AddScoped<{service}, global::TestAssembly.LoggingOrderRepository>();", generated);
        Assert.DoesNotContain($"Services.AddScoped<{service}, global::TestAssembly.OrderRepository>();", generated);
    }

    [Fact]
    public void Decorator_FluentDecorateOnPackageInterface_WrapsPackageImplementation()
    {
        var contracts = Contracts("""
                public interface IClock { }
                public class SystemClock : IClock { }
            """);

        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using MyCompany.Contracts;

            namespace TestAssembly
            {
                public class CachingClock : IClock
                {
                    public CachingClock(IClock inner) { }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IClock>(Lifetime.Singleton);
                        services.Decorate<IClock, CachingClock>();
                    }
                }
            }
            """;

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddKeyedSingleton<{Ns}.IClock, {Ns}.SystemClock>(\"__BASE__\");", generated);
        Assert.Contains("new global::TestAssembly.CachingClock(", generated);
        Assert.DoesNotContain($"Services.AddSingleton<{Ns}.IClock, global::TestAssembly.CachingClock>();", generated);
    }

    [Fact]
    public void Decorator_PackageInterface_ResolvesDecoratedInstanceAtRuntime()
    {
        var contracts = Contracts("""
                public interface IClock { string Describe(); }
                public class SystemClock : IClock { public string Describe() => "system"; }
            """);

        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using MyCompany.Contracts;

            namespace TestAssembly
            {
                public class CachingClock : IClock
                {
                    private readonly IClock _inner;
                    public CachingClock(IClock inner) { _inner = inner; }
                    public string Describe() => "caching(" + _inner.Describe() + ")";
                }

                public static class Clocks
                {
                    public static string Resolve()
                    {
                        var services = new ServiceCollection();
                        services.AddServicefyConventions().ByBaseType<IClock>(Lifetime.Singleton);
                        services.Decorate<IClock, CachingClock>();

                        using var provider = services.BuildServiceProvider();
                        return provider.GetRequiredService<IClock>().Describe();
                    }
                }
            }
            """;

        var resolved = PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Clocks", "Resolve", contracts);

        Assert.Equal("caching(system)", resolved);
    }

    [Fact]
    public void Decorator_DecoratorsShippedInPackage_AreNotRegisteredAsImplementations()
    {
        var contracts = Contracts(
            usings: "using System;",
            body: """
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                public sealed class DecoratorForAttribute<TService> : Attribute { }

                [AttributeUsage(AttributeTargets.Class, Inherited = false)]
                public sealed class DecoratorAttribute : Attribute { }

                public interface IPlugin { }

                public class DefaultPlugin : IPlugin { }

                [DecoratorFor<IPlugin>]
                public class TracingPlugin : IPlugin
                {
                    public TracingPlugin(IPlugin inner) { }
                }

                [Decorator]
                public class AuditingPlugin : IPlugin
                {
                    public AuditingPlugin(IPlugin inner) { }
                }
            """);

        var source = Consumer(types: "", rules: ".ByBaseType<IPlugin>(Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        Assert.Empty(result.CompilationErrors);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"{Ns}.DefaultPlugin", generated);
        Assert.DoesNotContain($"Services.AddScoped<{Ns}.IPlugin, {Ns}.TracingPlugin>();", generated);
        Assert.DoesNotContain($"Services.AddScoped<{Ns}.IPlugin, {Ns}.AuditingPlugin>();", generated);
    }

    [Fact]
    public void Decorator_FluentDecorateWithPackageDecorator_DecoratorIsNotAnImplementation()
    {
        var contracts = Contracts("""
                public interface IClock { }
                public class SystemClock : IClock { }
                public class CachingClock : IClock
                {
                    public CachingClock(IClock inner) { }
                }
            """);

        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using MyCompany.Contracts;

            namespace TestAssembly
            {
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByBaseType<IClock>(Lifetime.Singleton);
                        services.Decorate<IClock, CachingClock>();
                    }
                }
            }
            """;

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddKeyedSingleton<{Ns}.IClock, {Ns}.SystemClock>(\"__BASE__\");", generated);
        Assert.Contains($"new {Ns}.CachingClock(", generated);
        Assert.DoesNotContain($"Services.AddSingleton<{Ns}.IClock, {Ns}.CachingClock>();", generated);
        Assert.DoesNotContain($"Services.AddKeyedSingleton<{Ns}.IClock, {Ns}.CachingClock>(\"__BASE__\");", generated);
    }

    [Fact]
    public void Decorator_PackageAndConsumerImplementations_SkipDecoratorChain()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var source = Consumer(
            types: """
                    public class ExportPlugin : IPlugin { }

                    [DecoratorFor<IPlugin>]
                    public class LoggingPlugin : IPlugin
                    {
                        public LoggingPlugin(IPlugin inner) { }
                    }
                """,
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.CompilationErrors);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", generated);
        Assert.DoesNotContain("AddKeyedScoped", generated);
        Assert.DoesNotContain("global::TestAssembly.LoggingPlugin>", generated);
    }

    [Fact]
    public void NonAnchorPackage_DependingOnAnchor_ImplementationsAreIgnored()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var extras = ReferencedAssembly.Package("MyCompany.Extras", """
            namespace MyCompany.Extras
            {
                public class ExtraPlugin : MyCompany.Contracts.IPlugin { }
            }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : IPlugin { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts, extras);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", generated);
        Assert.DoesNotContain("ExtraPlugin", generated);
    }

    [Fact]
    public void NonAnchorPackage_InterfacesAreNotRegistrable()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
            """);

        var other = ReferencedAssembly.Package("Other.Auditing", """
            namespace Other.Auditing
            {
                public interface IAuditable { }
            }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : IPlugin, Other.Auditing.IAuditable { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts, other);

        AssertCleanBuild(result);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", result.ByBaseTypeSource);
        Assert.DoesNotContain("IAuditable", result.ByBaseTypeSource);
    }

    [Fact]
    public void AnchorDependency_InterfacesAreNotRegistrable()
    {
        var core = ReferencedAssembly.Package("MyCompany.Core", """
            namespace MyCompany.Core
            {
                public interface IComponent { }
            }
            """);

        var contracts = Contracts("""
                public interface IPlugin : MyCompany.Core.IComponent { }
                public interface IConfigurable { }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : IPlugin, IConfigurable { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped, ServiceTypeSelector.AllImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, core, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IConfigurable, global::TestAssembly.ExportPlugin>();", generated);
        Assert.DoesNotContain("IComponent", generated);
    }

    [Fact]
    public void AnchorIsExactlyTheAssemblyDeclaringTBase_NeitherItsDependenciesNorItsDependents()
    {
        var core = ReferencedAssembly.Package("MyCompany.Core", """
            namespace MyCompany.Core
            {
                public abstract class ComponentBase { }
                public class CoreComponent : ComponentBase { }
            }
            """);

        var contracts = Contracts("""
                public abstract class PluginBase : MyCompany.Core.ComponentBase { }
                public class DefaultPlugin : PluginBase { }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : PluginBase { }",
            rules: """
                .ByBaseType<MyCompany.Core.ComponentBase>(Lifetime.Scoped, ServiceTypeSelector.Self)
                            .ByBaseType<PluginBase>(Lifetime.Transient, ServiceTypeSelector.Self)
                """);

        var result = PackageCompilationHelper.Run(source, core, contracts);

        AssertCleanBuild(result);

        var componentBranch = result.Branch("global::MyCompany.Core.ComponentBase");
        Assert.NotNull(componentBranch);
        Assert.Contains("Services.AddScoped<global::MyCompany.Core.CoreComponent>();", componentBranch);
        Assert.Contains("Services.AddScoped<global::TestAssembly.ExportPlugin>();", componentBranch);
        Assert.DoesNotContain("DefaultPlugin", componentBranch);

        var pluginBranch = result.Branch($"{Ns}.PluginBase");
        Assert.NotNull(pluginBranch);
        Assert.Contains($"Services.AddTransient<{Ns}.DefaultPlugin>();", pluginBranch);
        Assert.Contains("Services.AddTransient<global::TestAssembly.ExportPlugin>();", pluginBranch);
        Assert.DoesNotContain("CoreComponent", pluginBranch);
    }

    [Fact]
    public void TBaseFromBcl_DoesNotAnchorAnything_PackagesAndBclAreNotScanned()
    {
        var contracts = Contracts("""
                public class PooledResource : System.IDisposable
                {
                    public void Dispose() { }
                }
            """);

        var source = Consumer(
            types: """
                    public class FileHandle : IDisposable
                    {
                        public void Dispose() { }
                    }
                """,
            rules: ".ByBaseType<IDisposable>(Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains("Services.AddScoped<global::System.IDisposable, global::TestAssembly.FileHandle>();", generated);
        Assert.DoesNotContain("PooledResource", generated);
        Assert.DoesNotContain("global::System.IO.", generated);
        Assert.DoesNotContain("global::Microsoft.", generated);
    }

    [Fact]
    public void TBaseFromBcl_ImplementedInterfaces_DoesNotRegisterBclInterfaces()
    {
        var source = Consumer(
            types: """
                    public interface IFileStore { }
                    public class FileStore : IFileStore, IDisposable
                    {
                        public void Dispose() { }
                    }
                """,
            rules: ".ByBaseType<IDisposable>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)",
            usings: "");

        var result = PackageCompilationHelper.Run(source);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains("Services.AddScoped<global::TestAssembly.IFileStore, global::TestAssembly.FileStore>();", generated);
        Assert.DoesNotContain("Services.AddScoped<global::System.IDisposable,", generated);
    }

    [Fact]
    public void TBaseObject_DoesNotScanPackagesOrTheBcl()
    {
        var contracts = Contracts("""
                public class PackageWidget { }
            """);

        var source = Consumer(
            types: "    public class LocalWidget { }",
            rules: ".ByBaseType<object>(Lifetime.Transient, ServiceTypeSelector.Self)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.NotNull(generated);
        Assert.Contains("Services.AddTransient<global::TestAssembly.LocalWidget>();", generated);
        Assert.DoesNotContain("PackageWidget", generated);
        Assert.DoesNotContain("global::System.", generated.Replace("typeof(global::System.Object)", "", StringComparison.Ordinal).Replace("typeof(object)", "", StringComparison.Ordinal));
        Assert.DoesNotContain("global::Microsoft.", generated);
    }

    [Fact]
    public void PackageReferencedButTBaseDeclaredInConsumer_PackageIsNotScanned()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var source = Consumer(
            types: """
                    public interface IHandler { }
                    public class OrderHandler : IHandler, IPlugin { }
                """,
            rules: ".ByBaseType<IHandler>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains("Services.AddScoped<global::TestAssembly.IHandler, global::TestAssembly.OrderHandler>();", generated);
        Assert.DoesNotContain("IPlugin", generated);
        Assert.DoesNotContain("BuiltInPlugin", generated);
    }

    [Fact]
    public void ProjectReference_ImplementingPackageInterface_IsRegisteredAgainstPackageInterface()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
                public class Order { }
            """);

        var features = ReferencedAssembly.Project("MyApp.Features", """
            namespace MyApp.Features
            {
                public interface IFeatureMarker { }
                public class SqlOrderRepository : MyCompany.Contracts.IRepository<MyCompany.Contracts.Order>, IFeatureMarker { }
            }
            """);

        var source = Consumer(
            types: "    public class CachedOrderRepository : IRepository<Order> { }",
            rules: ".ByBaseType<IRepository<Order>>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts, features);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        var service = $"{Ns}.IRepository<{Ns}.Order>";
        Assert.Contains($"Services.AddScoped<{service}, global::MyApp.Features.SqlOrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{service}, global::TestAssembly.CachedOrderRepository>();", generated);
        Assert.Contains("Services.AddScoped<global::MyApp.Features.IFeatureMarker, global::MyApp.Features.SqlOrderRepository>();", generated);
    }

    [Fact]
    public void TBaseFromProjectReference_DoesNotMakePackageInterfacesRegistrable()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var features = ReferencedAssembly.Project("MyApp.Features", """
            namespace MyApp.Features
            {
                public interface IFeature { }
                public abstract class FeaturePluginBase : MyCompany.Contracts.IPlugin, IFeature { }
                public class SearchPlugin : FeaturePluginBase { }
            }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : MyApp.Features.FeaturePluginBase { }",
            rules: ".ByBaseType<MyApp.Features.FeaturePluginBase>(Lifetime.Scoped, ServiceTypeSelector.AllImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts, features);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains("Services.AddScoped<global::MyApp.Features.IFeature, global::MyApp.Features.SearchPlugin>();", generated);
        Assert.Contains("Services.AddScoped<global::MyApp.Features.IFeature, global::TestAssembly.ExportPlugin>();", generated);
        Assert.DoesNotContain("IPlugin", generated);
        Assert.DoesNotContain("BuiltInPlugin", generated);
    }

    [Fact]
    public void MultipleRules_AnchorInterfacesAreRegistrableOnlyForTheAnchoringRule()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
            """);

        var source = Consumer(
            types: """
                    public interface IHandler { }
                    public class OrderHandler : IHandler, IPlugin { }
                """,
            rules: """
                .ByBaseType<IPlugin>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)
                            .ByBaseType<IHandler>(Lifetime.Transient, ServiceTypeSelector.ImplementedInterfaces)
                """);

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);

        var pluginBranch = result.Branch($"{Ns}.IPlugin");
        Assert.NotNull(pluginBranch);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.OrderHandler>();", pluginBranch);
        Assert.Contains("Services.AddScoped<global::TestAssembly.IHandler, global::TestAssembly.OrderHandler>();", pluginBranch);

        var handlerBranch = result.Branch("global::TestAssembly.IHandler");
        Assert.NotNull(handlerBranch);
        Assert.Contains("Services.AddTransient<global::TestAssembly.IHandler, global::TestAssembly.OrderHandler>();", handlerBranch);
        Assert.DoesNotContain("IPlugin", handlerBranch);
    }

    [Fact]
    public void MultipleRules_AnchorTypesAreCandidatesOnlyForTheAnchoringRule()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
                public class PluginResource : System.IDisposable
                {
                    public void Dispose() { }
                }
            """);

        var source = Consumer(
            types: """
                    public class FileHandle : IDisposable
                    {
                        public void Dispose() { }
                    }
                """,
            rules: """
                .ByBaseType<IPlugin>(Lifetime.Singleton)
                            .ByBaseType<IDisposable>(Lifetime.Scoped)
                """);

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);

        var pluginBranch = result.Branch($"{Ns}.IPlugin");
        Assert.NotNull(pluginBranch);
        Assert.Contains($"Services.AddSingleton<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", pluginBranch);

        var disposableBranch = result.Branch("global::System.IDisposable");
        Assert.NotNull(disposableBranch);
        Assert.Contains("Services.AddScoped<global::System.IDisposable, global::TestAssembly.FileHandle>();", disposableBranch);
        Assert.DoesNotContain("PluginResource", disposableBranch);
    }

    [Fact]
    public void MultipleRules_AnchoredOnDifferentPackages_EachScansOnlyItsOwnPackage()
    {
        var plugins = ReferencedAssembly.Package("Vendor.Plugins", """
            namespace Vendor.Plugins
            {
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            }
            """);

        var exporters = ReferencedAssembly.Package("Vendor.Exporters", """
            namespace Vendor.Exporters
            {
                public interface IExporter { }
                public class CsvExporter : IExporter { }
            }
            """);

        var source = Consumer(
            types: "    public class PdfExportPlugin : Vendor.Plugins.IPlugin, Vendor.Exporters.IExporter { }",
            rules: """
                .ByBaseType<Vendor.Plugins.IPlugin>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)
                            .ByBaseType<Vendor.Exporters.IExporter>(Lifetime.Singleton, ServiceTypeSelector.ImplementedInterfaces)
                """,
            usings: "");

        var result = PackageCompilationHelper.Run(source, plugins, exporters);

        AssertCleanBuild(result);

        var pluginBranch = result.Branch("global::Vendor.Plugins.IPlugin");
        Assert.NotNull(pluginBranch);
        Assert.Contains("Services.AddScoped<global::Vendor.Plugins.IPlugin, global::Vendor.Plugins.BuiltInPlugin>();", pluginBranch);
        Assert.Contains("Services.AddScoped<global::Vendor.Plugins.IPlugin, global::TestAssembly.PdfExportPlugin>();", pluginBranch);
        Assert.DoesNotContain("Exporter", pluginBranch);

        var exporterBranch = result.Branch("global::Vendor.Exporters.IExporter");
        Assert.NotNull(exporterBranch);
        Assert.Contains("Services.AddSingleton<global::Vendor.Exporters.IExporter, global::Vendor.Exporters.CsvExporter>();", exporterBranch);
        Assert.Contains("Services.AddSingleton<global::Vendor.Exporters.IExporter, global::TestAssembly.PdfExportPlugin>();", exporterBranch);
        Assert.DoesNotContain("Vendor.Plugins", exporterBranch);
    }

    [Fact]
    public void MultipleRules_SameAnchorPackage_DifferentTBasesAndSelectors_AllRegister()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public interface IClock { }
                public class BuiltInPlugin : IPlugin { }
                public class SystemClock : IClock { }
            """);

        var source = Consumer(
            types: "",
            rules: """
                .ByBaseType<IPlugin>(Lifetime.Scoped)
                            .ByBaseType<IPlugin>(Lifetime.Singleton, ServiceTypeSelector.SelfWithInterfaces)
                            .ByBaseType<IClock>(Lifetime.Singleton, ServiceTypeSelector.ImplementedInterfaces)
                """);

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);

        var pluginBaseType = result.Branch($"{Ns}.IPlugin", "BaseType");
        Assert.NotNull(pluginBaseType);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", pluginBaseType);
        Assert.DoesNotContain("SystemClock", pluginBaseType);

        var pluginSelf = result.Branch($"{Ns}.IPlugin", "SelfWithInterfaces");
        Assert.NotNull(pluginSelf);
        Assert.Contains($"Services.AddSingleton<{Ns}.BuiltInPlugin>();", pluginSelf);
        Assert.Contains($"Services.AddSingleton<{Ns}.IPlugin>(sp => sp.GetRequiredService<{Ns}.BuiltInPlugin>());", pluginSelf);

        var clock = result.Branch($"{Ns}.IClock");
        Assert.NotNull(clock);
        Assert.Contains($"Services.AddSingleton<{Ns}.IClock, {Ns}.SystemClock>();", clock);
        Assert.DoesNotContain("BuiltInPlugin", clock);
    }

    [Fact]
    public void OpenGenericRule_PackageDefinition_IsNotAnchored()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
                public class PackageOrder { }
                public class InMemoryRepository<T> : IRepository<T> { }
                public class PackageOrderRepository : IRepository<PackageOrder> { }
            """);

        var source = Consumer(
            types: """
                    public class Order { }
                    public class OrderRepository : IRepository<Order> { }
                """,
            rules: ".ByBaseType(typeof(IRepository<>), Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IRepository<global::TestAssembly.Order>, global::TestAssembly.OrderRepository>();", generated);
        Assert.DoesNotContain("InMemoryRepository", generated);
        Assert.DoesNotContain("PackageOrderRepository", generated);
    }

    [Fact]
    public void ClosedTypeofRule_PackageDefinition_IsAnchoredLikeTheGenericOverload()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
                public class Order { }
                public class InMemoryOrderRepository : IRepository<Order> { }
            """);

        var source = Consumer(
            types: "    public class SqlOrderRepository : IRepository<Order> { }",
            rules: ".ByBaseType(typeof(IRepository<Order>), Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddScoped<{Ns}.IRepository<{Ns}.Order>, {Ns}.InMemoryOrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IRepository<{Ns}.Order>, global::TestAssembly.SqlOrderRepository>();", generated);
    }

    [Fact]
    public void ClosedGenericRule_OpenGenericPackageImplementation_IsNotACandidate()
    {
        var contracts = Contracts("""
                public interface IRepository<T> { }
                public class Order { }
                public class InMemoryRepository<T> : IRepository<T> { }
            """);

        var source = Consumer(
            types: "    public class SqlOrderRepository : IRepository<Order> { }",
            rules: ".ByBaseType<IRepository<Order>>(Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Contains($"Services.AddScoped<{Ns}.IRepository<{Ns}.Order>, global::TestAssembly.SqlOrderRepository>();", result.ByBaseTypeSource);
        Assert.DoesNotContain("InMemoryRepository", result.ByBaseTypeSource);
    }

    [Theory]
    [InlineData(@"C:\Users\test\.nuget\packages\{0}\1.0.0\lib\net8.0\{1}.dll")]
    [InlineData("/Users/test/.NuGet/Packages/{0}/1.0.0/lib/net8.0/{1}.dll")]
    [InlineData("/home/test/.nuget/packages/{0}/2.1.0-preview.1/lib/netstandard2.0/{1}.dll")]
    public void PackageDetection_ByGlobalPackagesFolderPath(string pathFormat)
    {
        string PathFor(string name) => string.Format(pathFormat, name.ToLowerInvariant(), name);

        var contracts = ReferencedAssembly.Package(ContractsName, """
            namespace MyCompany.Contracts
            {
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            }
            """, PathFor(ContractsName));

        var extras = ReferencedAssembly.Package("MyCompany.Extras", """
            namespace MyCompany.Extras
            {
                public interface IExtraMarker { }
                public class ExtraPlugin : MyCompany.Contracts.IPlugin { }
            }
            """, PathFor("MyCompany.Extras"));

        var source = Consumer(
            types: "    public class ExportPlugin : IPlugin, MyCompany.Extras.IExtraMarker { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts, extras);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", generated);
        Assert.DoesNotContain("ExtraPlugin", generated);
        Assert.DoesNotContain("IExtraMarker", generated);
    }

    [Fact]
    public void PackageDetection_SameAssemblyAsProjectReference_IsScannedWholesale()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
            """);

        var extras = ReferencedAssembly.Project("MyCompany.Extras", """
            namespace MyCompany.Extras
            {
                public interface IExtraMarker { }
                public class ExtraPlugin : MyCompany.Contracts.IPlugin { }
            }
            """);

        var source = Consumer(
            types: "    public class ExportPlugin : IPlugin, MyCompany.Extras.IExtraMarker { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped, ServiceTypeSelector.ImplementedInterfaces)");

        var result = PackageCompilationHelper.Run(source, contracts, extras);

        AssertCleanBuild(result);
        var generated = result.ByBaseTypeSource;
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::MyCompany.Extras.ExtraPlugin>();", generated);
        Assert.Contains("Services.AddScoped<global::MyCompany.Extras.IExtraMarker, global::TestAssembly.ExportPlugin>();", generated);
    }

    [Fact]
    public void Incremental_EditsKeepAnchorScanCorrect_AndRemovingTheAnchoringRuleDropsPackageTypes()
    {
        var contracts = Contracts("""
                public interface IPlugin { }
                public class BuiltInPlugin : IPlugin { }
            """);

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13);
        var references = PackageCompilationHelper.BuildReferences(parseOptions, [contracts]);

        const string path = "Consumer.cs";
        var initialSource = Consumer(
            types: "    public class ExportPlugin : IPlugin { }",
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped)");

        Compilation compilation = CSharpCompilation.Create(
            assemblyName: PackageCompilationHelper.AssemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(initialSource, parseOptions, path: path, cancellationToken: TestContext.Current.CancellationToken)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new IIncrementalGenerator[] { new ServicefyByConfigurationGenerator(), new ServicefyConventionsGenerator() }
                .Select(g => g.AsSourceGenerator()),
            parseOptions: parseOptions);

        string? ByBaseType(GeneratorDriver d) => d.GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByBaseType.g.cs", StringComparison.Ordinal))
            ?.GetText(TestContext.Current.CancellationToken).ToString();

        Compilation Edit(Compilation c, string newSource)
        {
            var tree = c.SyntaxTrees.Single(t => t.FilePath == path);
            return c.ReplaceSyntaxTree(tree, tree.WithChangedText(SourceText.From(newSource)));
        }

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var first = ByBaseType(driver);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", first);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", first);

        compilation = Edit(compilation, Consumer(
            types: """
                    public class ExportPlugin : IPlugin { }
                    public class ImportPlugin : IPlugin { }
                """,
            rules: ".ByBaseType<IPlugin>(Lifetime.Scoped)"));
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var second = ByBaseType(driver);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, {Ns}.BuiltInPlugin>();", second);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ExportPlugin>();", second);
        Assert.Contains($"Services.AddScoped<{Ns}.IPlugin, global::TestAssembly.ImportPlugin>();", second);

        compilation = Edit(compilation, Consumer(
            types: """
                    public class ExportPlugin : IPlugin, IDisposable
                    {
                        public void Dispose() { }
                    }
                """,
            rules: ".ByBaseType<IDisposable>(Lifetime.Scoped)"));
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var third = ByBaseType(driver);
        Assert.NotNull(third);
        Assert.Contains("Services.AddScoped<global::System.IDisposable, global::TestAssembly.ExportPlugin>();", third);
        Assert.DoesNotContain("BuiltInPlugin", third);
        Assert.DoesNotContain("IPlugin", third);
    }
}
