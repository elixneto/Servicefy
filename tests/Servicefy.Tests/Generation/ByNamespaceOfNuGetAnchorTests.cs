using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Servicefy.Package.ByConfiguration.Generators;
using Servicefy.Package.Conventions.Generators;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class ByNamespaceOfNuGetAnchorTests
{
    private const string ContractsName = "MyCompany.Contracts";
    private const string Ns = "global::MyCompany.Contracts";
    private const string Repos = "global::MyCompany.Contracts.Repositories";
    private const string Marker = "global::MyCompany.Contracts.ContractsMarker";

    private static ReferencedAssembly Contracts(string source) =>
        ReferencedAssembly.Package(ContractsName, "using System;\n" + source);

    private static string Consumer(string types, string rules) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using TestAssembly;

        {{types}}

        namespace TestAssembly.Composition
        {
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

    private const string DocsPackageSource = """
        namespace MyCompany.Contracts { public class ContractsMarker { } }
        namespace MyCompany.Contracts.Repositories { public interface IOrderRepository { } }
        """;

    private static void AssertCleanBuild(PackageRunResult result)
    {
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationErrors);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void DocsExample_ConsumerImplementationUnderMarkerTree_IsRegisteredAgainstPackageInterface()
    {
        var contracts = Contracts(DocsPackageSource);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class OrderRepository : IOrderRepository { }
                }

                namespace MyApp.Repositories
                {
                    public class LegacyOrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }
                """,
            rules: """.ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped)""");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains(
            $"if (markerType == typeof({Marker}) && lifetime == Lifetime.Scoped && predicateExpression == \"ns => ns.EndsWith(\\\".Repositories\\\")\")",
            generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.OrderRepository>();", generated);
        Assert.DoesNotContain("LegacyOrderRepository", generated);
        Assert.Equal(1, CountOccurrences(generated, "Services.Add"));
    }

    [Fact]
    public void DocsExample_ResolvesConsumerImplementationAtRuntime()
    {
        var contracts = Contracts(DocsPackageSource);

        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using TestAssembly;

            namespace MyCompany.Contracts.Repositories
            {
                public class OrderRepository : IOrderRepository { }
            }

            namespace MyApp.Repositories
            {
                public class LegacyOrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }

            namespace TestAssembly
            {
                public static class Acceptance
                {
                    public static string Resolve()
                    {
                        var services = new ServiceCollection();
                        services.AddServicefyConventions()
                            .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped);

                        using var provider = services.BuildServiceProvider();
                        using var scope = provider.CreateScope();
                        var names = new System.Collections.Generic.List<string>();
                        foreach (var repository in scope.ServiceProvider.GetServices<MyCompany.Contracts.Repositories.IOrderRepository>())
                            names.Add(repository.GetType().FullName);
                        return string.Join(",", names);
                    }
                }
            }
            """;

        var resolved = PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Acceptance", "Resolve", contracts);

        Assert.Equal("MyCompany.Contracts.Repositories.OrderRepository", resolved);
    }

    [Fact]
    public void ConsumerImplementationsOutsideRootNamespace_AreNotPickedUp_EvenWithTruePredicate()
    {
        var contracts = Contracts(DocsPackageSource);

        var source = Consumer(
            types: """
                namespace TestAssembly.Repositories
                {
                    public class OrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }

                namespace MyCompany
                {
                    // The parent of the root namespace is outside the subtree.
                    public class ParentNamespaceRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }

                namespace MyCompany.ContractsExtra.Repositories
                {
                    // Shares the root's string prefix, but is not a sub-namespace of it.
                    public class PrefixSiblingRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }

                namespace MyCompany.Other.Repositories
                {
                    public class SiblingRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Null(result.ByNamespaceOfSource);
    }

    [Fact]
    public void ConsumerImplementations_InRootAndDeepSubNamespaces_AreCandidates()
    {
        var contracts = Contracts(DocsPackageSource);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts
                {
                    public class RootRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }

                namespace MyCompany.Contracts.Data.Sql.Repositories
                {
                    public class DeepRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }

                namespace MyCompany.ContractsExtra
                {
                    public class PrefixSiblingRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Transient)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddTransient<{Repos}.IOrderRepository, {Ns}.RootRepository>();", generated);
        Assert.Contains($"Services.AddTransient<{Repos}.IOrderRepository, {Ns}.Data.Sql.Repositories.DeepRepository>();", generated);
        Assert.DoesNotContain("PrefixSiblingRepository", generated);
    }

    [Fact]
    public void ConsumerTypesUnderMarkerTree_OnlyRegistrableInterfacesCount()
    {
        var contracts = Contracts(DocsPackageSource);

        var other = ReferencedAssembly.Package("Acme.Tools", """
            namespace Acme.Tools { public interface IToolService { } }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public interface IAuditable { }

                    public class OrderRepository : IOrderRepository, IAuditable, IDisposable, Acme.Tools.IToolService
                    {
                        public void Dispose() { }
                    }

                    public class DisposableOnly : IDisposable
                    {
                        public void Dispose() { }
                    }

                    public class OtherPackageOnly : Acme.Tools.IToolService { }

                    public class NoInterfaces { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts, other);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.OrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IAuditable, {Repos}.OrderRepository>();", generated);
        Assert.DoesNotContain("IDisposable", generated);
        Assert.DoesNotContain("IToolService", generated);
        Assert.DoesNotContain("DisposableOnly", generated);
        Assert.DoesNotContain("OtherPackageOnly", generated);
        Assert.DoesNotContain("NoInterfaces", generated);
        Assert.Equal(2, CountOccurrences(generated, "Services.Add"));
    }

    [Fact]
    public void ConsumerTypes_DirectPackageInterfacesOnly_InheritedOnesAreNotRegistered()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IRepository { }
                public interface IOrderRepository : IRepository { }
                public interface IRepository<T> { }
                public abstract class RepositoryBase : IOrderRepository { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class Order { }

                    // Directly implements IOrderRepository (which extends IRepository) and a closed generic.
                    public class OrderRepository : IOrderRepository, IRepository<Order> { }

                    // Implements IOrderRepository only through its base class: no direct interfaces.
                    public class DerivedRepository : RepositoryBase { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.OrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IRepository<{Repos}.Order>, {Repos}.OrderRepository>();", generated);
        Assert.DoesNotContain($"Services.AddScoped<{Repos}.IRepository, ", generated);
        Assert.DoesNotContain("DerivedRepository", generated);
        Assert.DoesNotContain("RepositoryBase", generated);
    }

    [Fact]
    public void AnchorPackageInterfaces_OutsideMarkerTree_AreRegistrableToo()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Shared { public interface IClock { } }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Time
                {
                    public class SystemClock : MyCompany.Shared.IClock { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Singleton)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Contains(
            $"Services.AddSingleton<global::MyCompany.Shared.IClock, {Ns}.Time.SystemClock>();",
            result.ByNamespaceOfSource);
    }

    [Fact]
    public void PackageImplementationsUnderMarkerTree_AreRegisteredAlongsideConsumerImplementations()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            namespace MyCompany.Contracts.Services
            {
                public interface IPricingService { }
                public class DefaultPricingService : IPricingService { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository { }
                }
                """,
            rules: """.ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped)""");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", generated);
        Assert.DoesNotContain("DefaultPricingService", generated);
    }

    [Theory]
    [InlineData("_ => true")]
    [InlineData("ns => ns.EndsWith(\".Repositories\")")]
    [InlineData("ns => ns.StartsWith(\"MyCompany\") || ns.StartsWith(\"Other\") || ns.StartsWith(\"Unrelated\")")]
    public void PackageTypesOutsideMarkerTree_AreNeverScanned_WhateverThePredicate(string predicate)
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }

            // Same package, outside the marker's root-namespace subtree.
            namespace MyCompany
            {
                public class ParentNamespaceRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }
            namespace MyCompany.ContractsExtra.Repositories
            {
                public class PrefixSiblingRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }
            namespace MyCompany.Internal.Repositories
            {
                public interface IInternalRepository { }
                public class InternalStoreRepository : IInternalRepository, MyCompany.Contracts.Repositories.IOrderRepository { }
            }
            namespace Other.Repositories
            {
                public class OtherRootRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }
            namespace Unrelated
            {
                public interface IUnrelatedService { }
                public class UnrelatedService : IUnrelatedService { }
            }
            """);

        var source = Consumer(types: "", rules: $".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>({predicate}, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", generated);
        Assert.DoesNotContain("ParentNamespaceRepository", generated);
        Assert.DoesNotContain("PrefixSiblingRepository", generated);
        Assert.DoesNotContain("InternalStoreRepository", generated);
        Assert.DoesNotContain("IInternalRepository", generated);
        Assert.DoesNotContain("OtherRootRepository", generated);
        Assert.DoesNotContain("UnrelatedService", generated);
        Assert.Equal(1, CountOccurrences(generated, "Services.Add"));
    }

    [Fact]
    public void MarkerInPackageSubNamespace_PrunesToThatSubNamespace_NotTheTopLevelNamespace()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts
            {
                public interface IRootService { }
                public class RootService : IRootService { }
            }
            namespace MyCompany.Contracts.Repositories
            {
                public class RepositoriesMarker { }
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            namespace MyCompany.Contracts.Repositories.Caching
            {
                public class CachedOrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }
            namespace MyCompany.Contracts.Services
            {
                public interface IPricingService { }
                public class DefaultPricingService : IPricingService { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository { }
                }
                namespace MyCompany.Contracts.Services
                {
                    public class ConsumerPricingService : IPricingService { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.Repositories.RepositoriesMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.Caching.CachedOrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", generated);
        Assert.DoesNotContain("RootService", generated);
        Assert.DoesNotContain("DefaultPricingService", generated);
        Assert.DoesNotContain("ConsumerPricingService", generated);
        Assert.Equal(3, CountOccurrences(generated, "Services.Add"));
    }

    [Fact]
    public void PackageCandidates_OnlyPublicConcreteNonGenericClasses_AreRegistered()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Plugins
            {
                public interface IPlugin { }

                public class VisiblePlugin : IPlugin { }
                internal class HiddenPlugin : IPlugin { }
                public abstract class AbstractPlugin : IPlugin { }
                public class DerivedFromAbstractPlugin : AbstractPlugin { }
                public class OpenGenericPlugin<T> : IPlugin { }
                public struct ValueTypePlugin : IPlugin { }
                public static class StaticHelper { }
                public class NoInterfacePlugin { }
                public class DisposablePlugin : IDisposable { public void Dispose() { } }

                public class PublicOuter
                {
                    public class PublicNestedPlugin : IPlugin { }
                    internal class InternalNestedPlugin : IPlugin { }
                    private class PrivateNestedPlugin : IPlugin { }
                }

                internal class InternalOuter
                {
                    public class NestedInInternalOuterPlugin : IPlugin { }
                }
            }
            """);

        var source = Consumer(types: "", rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        const string plugins = Ns + ".Plugins";
        Assert.Contains($"Services.AddScoped<{plugins}.IPlugin, {plugins}.VisiblePlugin>();", generated);
        Assert.Contains($"Services.AddScoped<{plugins}.IPlugin, {plugins}.PublicOuter.PublicNestedPlugin>();", generated);

        Assert.DoesNotContain("DerivedFromAbstractPlugin", generated);
        Assert.DoesNotContain("HiddenPlugin", generated);
        Assert.DoesNotContain($"{plugins}.AbstractPlugin>", generated);
        Assert.DoesNotContain("OpenGenericPlugin", generated);
        Assert.DoesNotContain("ValueTypePlugin", generated);
        Assert.DoesNotContain("StaticHelper", generated);
        Assert.DoesNotContain("NoInterfacePlugin", generated);
        Assert.DoesNotContain("DisposablePlugin", generated);
        Assert.DoesNotContain("InternalNestedPlugin", generated);
        Assert.DoesNotContain("PrivateNestedPlugin", generated);
        Assert.DoesNotContain("NestedInInternalOuterPlugin", generated);
        Assert.DoesNotContain("ContractsMarker>", generated);
    }

    [Fact]
    public void PackageImplementation_InterfacesFromNonAnchorPackages_AreNotRegistered()
    {
        var core = ReferencedAssembly.Package("MyCompany.Core", """
            namespace MyCompany.Core { public interface ICoreService { } }
            """);

        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Services
            {
                public interface IPricingService { }
                public class DefaultPricingService : IPricingService, MyCompany.Core.ICoreService { }
                public class CoreOnlyService : MyCompany.Core.ICoreService { }
            }
            """);

        var source = Consumer(types: "", rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, core, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Ns}.Services.IPricingService, {Ns}.Services.DefaultPricingService>();", generated);
        Assert.DoesNotContain("ICoreService", generated);
        Assert.DoesNotContain("CoreOnlyService", generated);
    }

    [Fact]
    public void NonAnchorPackage_DeclaringTypesInMarkerNamespace_IsNotScanned_AndItsInterfacesAreNotRegistrable()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var extras = ReferencedAssembly.Package("MyCompany.Contracts.Extras", """
            namespace MyCompany.Contracts.Repositories
            {
                public interface IExtraRepository { }
                public class ExtraOrderRepository : IOrderRepository { }
                public class ExtraRepository : IExtraRepository { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository, IExtraRepository { }
                    public class ExtraOnlyRepository : IExtraRepository { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts, extras);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", generated);
        Assert.DoesNotContain("ExtraOrderRepository", generated);
        Assert.DoesNotContain("IExtraRepository", generated);
        Assert.DoesNotContain("ExtraOnlyRepository", generated);
        Assert.DoesNotContain($"{Repos}.ExtraRepository", generated);
        Assert.Equal(2, CountOccurrences(generated, "Services.Add"));
    }

    [Fact]
    public void AnchorDependency_WithTypesUnderMarkerNamespace_IsNotScanned()
    {
        var core = ReferencedAssembly.Package("MyCompany.Contracts.Core", """
            namespace MyCompany.Contracts.Core
            {
                public interface ICoreService { }
                public class CoreService : ICoreService { }
            }
            """);

        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Services
            {
                public interface IPricingService { }
                public class DefaultPricingService : IPricingService { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Core
                {
                    public class ConsumerCoreService : ICoreService { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, core, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Ns}.Services.IPricingService, {Ns}.Services.DefaultPricingService>();", generated);
        Assert.DoesNotContain("CoreService", generated);
    }

    [Fact]
    public void MarkerInConsumer_SameRootNamespaceAsPackage_PackageIsNotScanned_AndItsInterfacesAreNotRegistrable()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts
                {
                    public class LocalMarker { }
                }

                namespace MyCompany.Contracts.Repositories
                {
                    public interface IAuditable { }
                    public class SqlOrderRepository : IOrderRepository, IAuditable { }
                    public class PackageInterfaceOnlyRepository : IOrderRepository { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.LocalMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IAuditable, {Repos}.SqlOrderRepository>();", generated);
        Assert.DoesNotContain("IOrderRepository", generated);
        Assert.DoesNotContain("InMemoryOrderRepository", generated);
        Assert.DoesNotContain("PackageInterfaceOnlyRepository", generated);
    }

    [Fact]
    public void MarkerInProjectReference_KeepsRegularScope_PackageIsNotAnchored()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var features = ReferencedAssembly.Project("MyCompany.Features", """
            namespace MyCompany.Features
            {
                public class FeaturesMarker { }
            }
            namespace MyCompany.Features.Handlers
            {
                public interface IOrderHandler { }
                public class OrderHandler : IOrderHandler { }
                public class PackageBackedRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }
            namespace MyCompany.Elsewhere
            {
                public interface IElsewhere { }
                public class ElsewhereService : IElsewhere { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Features.Local
                {
                    public interface ILocalService { }
                    public class LocalService : ILocalService { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Features.FeaturesMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts, features);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains("Services.AddScoped<global::MyCompany.Features.Handlers.IOrderHandler, global::MyCompany.Features.Handlers.OrderHandler>();", generated);
        Assert.Contains("Services.AddScoped<global::MyCompany.Features.Local.ILocalService, global::MyCompany.Features.Local.LocalService>();", generated);
        Assert.DoesNotContain("PackageBackedRepository", generated);
        Assert.DoesNotContain("IOrderRepository", generated);
        Assert.DoesNotContain("ElsewhereService", generated);
    }

    [Fact]
    public void MarkerInBcl_DoesNotScanTheBcl_OnlyConsumerTypesUnderItsNamespaceMatch()
    {
        var contracts = Contracts(DocsPackageSource);

        var source = Consumer(
            types: """
                namespace System.Text.Custom
                {
                    public interface ITextFormatter { }
                    public class TextFormatter : ITextFormatter { }
                    public class PackageInterfaceRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }

                namespace TestAssembly.Text
                {
                    public interface IOutside { }
                    public class Outside : IOutside { }
                }
                """,
            rules: ".ByNamespaceOf<System.Text.StringBuilder>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains("Services.AddScoped<global::System.Text.Custom.ITextFormatter, global::System.Text.Custom.TextFormatter>();", generated);
        Assert.DoesNotContain("PackageInterfaceRepository", generated);
        Assert.DoesNotContain("Outside", generated);
        Assert.Equal(1, CountOccurrences(generated, "Services.Add"));
    }

    [Fact]
    public void MarkerInBcl_NoConsumerTypes_EmitsNoByNamespaceOfFile()
    {
        var source = Consumer(
            types: "",
            rules: ".ByNamespaceOf<System.Collections.Generic.List<int>>(_ => true, Lifetime.Singleton)");

        var result = PackageCompilationHelper.Run(source);

        AssertCleanBuild(result);
        Assert.Null(result.ByNamespaceOfSource);
    }

    [Fact]
    public void MarkerInPackageGlobalNamespace_WalksTheWholePackage_AndRestrictsNothing()
    {
        var contracts = Contracts("""
            public class GlobalContractsMarker { }

            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            namespace Other.Stuff
            {
                public interface IThing { }
                public class Thing : IThing { }
            }
            """);

        var source = Consumer(
            types: """
                namespace TestAssembly.Repositories
                {
                    public class SqlOrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }
                """,
            rules: ".ByNamespaceOf<GlobalContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", generated);
        Assert.Contains("Services.AddScoped<global::Other.Stuff.IThing, global::Other.Stuff.Thing>();", generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, global::TestAssembly.Repositories.SqlOrderRepository>();", generated);
    }

    [Fact]
    public void MarkerInConsumerGlobalNamespace_RestrictsNothing_LikeByNamespace()
    {
        var source = Consumer(
            types: """
                public class GlobalMarker { }

                namespace MyApp.Data.Repositories
                {
                    public interface IUserRepository { }
                    public class UserRepository : IUserRepository { }
                }

                namespace Other.Repositories
                {
                    public interface IOrderRepository { }
                    public class OrderRepository : IOrderRepository { }
                }
                """,
            rules: """.ByNamespaceOf<GlobalMarker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped)""");

        var result = PackageCompilationHelper.Run(source);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains("Services.AddScoped<global::MyApp.Data.Repositories.IUserRepository, global::MyApp.Data.Repositories.UserRepository>();", generated);
        Assert.Contains("Services.AddScoped<global::Other.Repositories.IOrderRepository, global::Other.Repositories.OrderRepository>();", generated);
    }

    private const string PredicatePackageSource = """
        namespace MyCompany.Contracts
        {
            public class ContractsMarker { }
            public interface IRootService { }
            public class RootService : IRootService { }
        }
        namespace MyCompany.Contracts.Repositories
        {
            public interface IOrderRepository { }
            public class InMemoryOrderRepository : IOrderRepository { }
        }
        namespace MyCompany.Contracts.Services
        {
            public interface IPricingService { }
            public class DefaultPricingService : IPricingService { }
        }
        namespace MyCompany.Contracts.Data
        {
            public interface IUnitOfWork { }
            public class UnitOfWork : IUnitOfWork { }
        }
        """;

    [Theory]
    [InlineData("ns => ns == \"MyCompany.Contracts\"", "RootService", "InMemoryOrderRepository,DefaultPricingService,UnitOfWork")]
    [InlineData("ns => ns.EndsWith(\".Repositories\")", "InMemoryOrderRepository", "RootService,DefaultPricingService,UnitOfWork")]
    [InlineData("ns => ns.StartsWith(\"MyCompany.Contracts.\")", "InMemoryOrderRepository,DefaultPricingService,UnitOfWork", "RootService")]
    [InlineData("ns => ns.Contains(\"Serv\")", "DefaultPricingService", "RootService,InMemoryOrderRepository,UnitOfWork")]
    [InlineData("ns => !ns.EndsWith(\".Services\")", "RootService,InMemoryOrderRepository,UnitOfWork", "DefaultPricingService")]
    [InlineData("ns => ns.EndsWith(\".Repositories\") || ns.EndsWith(\".Data\")", "InMemoryOrderRepository,UnitOfWork", "RootService,DefaultPricingService")]
    [InlineData("ns => ns.EndsWith(\".Services\") || ns == \"MyCompany.Contracts.Data\"", "DefaultPricingService,UnitOfWork", "RootService,InMemoryOrderRepository")]
    [InlineData("_ => true", "RootService,InMemoryOrderRepository,DefaultPricingService,UnitOfWork", "")]
    public void Predicate_IsEvaluatedAgainstEachPackageTypesFullNamespace(string predicate, string registered, string notRegistered)
    {
        var contracts = Contracts(PredicatePackageSource);

        var source = Consumer(types: "", rules: $".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>({predicate}, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        foreach (var name in registered.Split(',', StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains($".{name}>();", generated);
        foreach (var name in notRegistered.Split(',', StringSplitOptions.RemoveEmptyEntries))
            Assert.DoesNotContain($".{name}>", generated);
    }

    [Fact]
    public void Predicate_MatchingOnlyNamespacesOutsideMarkerTree_EmitsNoFile()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            namespace MyCompany.Internal
            {
                public interface IInternalService { }
                public class InternalService : IInternalService { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Internal.Consumer
                {
                    public class ConsumerInternalService : MyCompany.Internal.IInternalService { }
                }
                """,
            rules: """.ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.StartsWith("MyCompany.Internal"), Lifetime.Scoped)""");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Null(result.ByNamespaceOfSource);
    }

    [Fact]
    public void UnsupportedPredicate_OnPackageMarker_ReportsSVCFY012_AndEmitsNothing()
    {
        var contracts = Contracts(PredicatePackageSource);

        var source = Consumer(types: "", rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.Length > 5, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "SVCFY012");
        Assert.Null(result.ByNamespaceOfSource);
    }

    [Fact]
    public void MultipleRules_SamePackage_DifferentMarkerSubtrees_EachScansOnlyItsOwnSubtree()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts.Repositories
            {
                public class RepositoriesMarker { }
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            namespace MyCompany.Contracts.Services
            {
                public class ServicesMarker { }
                public interface IPricingService { }
                public class DefaultPricingService : IPricingService { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository { }
                }
                namespace MyCompany.Contracts.Services
                {
                    public class DiscountPricingService : IPricingService { }
                }
                """,
            rules: """
                .ByNamespaceOf<MyCompany.Contracts.Repositories.RepositoriesMarker>(_ => true, Lifetime.Scoped)
                        .ByNamespaceOf<MyCompany.Contracts.Services.ServicesMarker>(_ => true, Lifetime.Singleton)
                """);

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);

        var repositories = result.NamespaceOfBranch($"{Repos}.RepositoriesMarker", "Scoped");
        Assert.NotNull(repositories);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", repositories);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", repositories);
        Assert.DoesNotContain("PricingService", repositories);

        var services = result.NamespaceOfBranch($"{Ns}.Services.ServicesMarker", "Singleton");
        Assert.NotNull(services);
        Assert.Contains($"Services.AddSingleton<{Ns}.Services.IPricingService, {Ns}.Services.DefaultPricingService>();", services);
        Assert.Contains($"Services.AddSingleton<{Ns}.Services.IPricingService, {Ns}.Services.DiscountPricingService>();", services);
        Assert.DoesNotContain("OrderRepository", services);
    }

    [Fact]
    public void MultipleRules_SameMarker_DifferentPredicatesAndLifetimes_AllRegister()
    {
        var contracts = Contracts(PredicatePackageSource);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository { }
                }
                """,
            rules: """
                .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped)
                        .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Services"), Lifetime.Singleton)
                        .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Repositories"), Lifetime.Transient)
                """);

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);

        var scoped = result.NamespaceOfBranch(Marker, "Scoped");
        Assert.NotNull(scoped);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", scoped);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", scoped);
        Assert.DoesNotContain("PricingService", scoped);

        var singleton = result.NamespaceOfBranch(Marker, "Singleton");
        Assert.NotNull(singleton);
        Assert.Contains($"Services.AddSingleton<{Ns}.Services.IPricingService, {Ns}.Services.DefaultPricingService>();", singleton);
        Assert.DoesNotContain("OrderRepository", singleton);

        var transient = result.NamespaceOfBranch(Marker, "Transient");
        Assert.NotNull(transient);
        Assert.Contains($"Services.AddTransient<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", transient);
        Assert.Contains($"Services.AddTransient<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", transient);
    }

    [Fact]
    public void MultipleRules_DifferentMarkersInSamePackageNamespace_RegisterTheSameTypes()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts
            {
                public class ContractsMarker { }
                public class OtherMarker { }
            }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository { }
                }
                """,
            rules: """
                .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)
                        .ByNamespaceOf<MyCompany.Contracts.OtherMarker>(_ => true, Lifetime.Scoped)
                """);

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        foreach (var marker in new[] { Marker, $"{Ns}.OtherMarker" })
        {
            var branch = result.NamespaceOfBranch(marker);
            Assert.NotNull(branch);
            Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", branch);
            Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", branch);
            Assert.Equal(2, CountOccurrences(branch, "Services.Add"));
        }
    }

    [Fact]
    public void MultipleRules_DifferentPackages_EachScansOnlyItsOwnPackage()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var plugins = ReferencedAssembly.Package("Acme.Plugins", """
            namespace Acme.Plugins { public class PluginsMarker { } }
            namespace Acme.Plugins.Exporters
            {
                public interface IExporter { }
                public class CsvExporter : IExporter { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    // Also implements the other package's interface: not registrable for the Contracts rule.
                    public class SqlOrderRepository : IOrderRepository, Acme.Plugins.Exporters.IExporter { }
                }
                namespace Acme.Plugins.Exporters
                {
                    public class JsonExporter : IExporter { }
                }
                """,
            rules: """
                .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)
                        .ByNamespaceOf<Acme.Plugins.PluginsMarker>(_ => true, Lifetime.Singleton)
                """);

        var result = PackageCompilationHelper.Run(source, contracts, plugins);

        AssertCleanBuild(result);

        var contractsBranch = result.NamespaceOfBranch(Marker);
        Assert.NotNull(contractsBranch);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", contractsBranch);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", contractsBranch);
        Assert.DoesNotContain("Exporter", contractsBranch);

        var pluginsBranch = result.NamespaceOfBranch("global::Acme.Plugins.PluginsMarker");
        Assert.NotNull(pluginsBranch);
        Assert.Contains("Services.AddSingleton<global::Acme.Plugins.Exporters.IExporter, global::Acme.Plugins.Exporters.CsvExporter>();", pluginsBranch);
        Assert.Contains("Services.AddSingleton<global::Acme.Plugins.Exporters.IExporter, global::Acme.Plugins.Exporters.JsonExporter>();", pluginsBranch);
        Assert.DoesNotContain("OrderRepository", pluginsBranch);
    }

    [Fact]
    public void MultipleRules_PackageInterfacesAndTypesCountOnlyForTheAnchoringRule()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class LocalMarker { }
                    public interface IAuditable { }
                    public class SqlOrderRepository : IOrderRepository, IAuditable { }
                }
                """,
            rules: """
                .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)
                        .ByNamespaceOf<MyCompany.Contracts.Repositories.LocalMarker>(_ => true, Lifetime.Singleton)
                """);

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);

        var anchored = result.NamespaceOfBranch(Marker);
        Assert.NotNull(anchored);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", anchored);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", anchored);
        Assert.Contains($"Services.AddScoped<{Repos}.IAuditable, {Repos}.SqlOrderRepository>();", anchored);

        var local = result.NamespaceOfBranch($"{Repos}.LocalMarker");
        Assert.NotNull(local);
        Assert.Contains($"Services.AddSingleton<{Repos}.IAuditable, {Repos}.SqlOrderRepository>();", local);
        Assert.DoesNotContain("IOrderRepository", local);
        Assert.DoesNotContain("InMemoryOrderRepository", local);
    }

    [Fact]
    public void ByNamespaceOfAnchor_DoesNotLeakIntoByNamespaceRules()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public interface IAuditable { }
                    public class SqlOrderRepository : IOrderRepository, IAuditable { }
                }
                """,
            rules: """
                .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)
                        .ByNamespace(ns => ns.EndsWith(".Repositories"), Lifetime.Singleton)
                """);

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", result.ByNamespaceOfSource);

        var byNamespace = result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs");
        Assert.NotNull(byNamespace);
        Assert.Contains($"Services.AddSingleton<{Repos}.IAuditable, {Repos}.SqlOrderRepository>();", byNamespace);
        Assert.DoesNotContain("InMemoryOrderRepository", byNamespace);
        Assert.DoesNotContain("IOrderRepository", byNamespace);
    }

    [Fact]
    public void Decorator_ConsumerDecoratorForPackageInterface_WrapsPackageImplementation()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    // Under the marker's tree too: as a decorator it must not become an implementation.
                    [DecoratorFor<IOrderRepository>]
                    public class LoggingOrderRepository : IOrderRepository
                    {
                        public LoggingOrderRepository(IOrderRepository inner) { }
                    }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddKeyedScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>(\"__BASE__\");", generated);
        Assert.Contains($"new {Repos}.LoggingOrderRepository(", generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository>(sp =>", generated);
        Assert.DoesNotContain($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", generated);
        Assert.DoesNotContain($"{Repos}.LoggingOrderRepository>", generated);
    }

    [Fact]
    public void Decorator_FluentDecorateOnPackageInterface_ResolvesDecoratedInstanceAtRuntime()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Time
            {
                public interface IClock { string Describe(); }
                public class SystemClock : IClock { public string Describe() => "system"; }
            }
            """);

        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using MyCompany.Contracts.Time;
            using TestAssembly;

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
                        services.AddServicefyConventions()
                            .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Time"), Lifetime.Singleton);
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
        var contracts = Contracts("""
            namespace MyCompany.Contracts
            {
                public class ContractsMarker { }

                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                public sealed class DecoratorForAttribute<TService> : Attribute { }

                [AttributeUsage(AttributeTargets.Class, Inherited = false)]
                public sealed class DecoratorAttribute : Attribute { }
            }
            namespace MyCompany.Contracts.Plugins
            {
                public interface IPlugin { }

                public class DefaultPlugin : IPlugin { }

                [MyCompany.Contracts.DecoratorFor<IPlugin>]
                public class TracingPlugin : IPlugin
                {
                    public TracingPlugin(IPlugin inner) { }
                }

                [MyCompany.Contracts.Decorator]
                public class AuditingPlugin : IPlugin
                {
                    public AuditingPlugin(IPlugin inner) { }
                }
            }
            """);

        var source = Consumer(types: "", rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        Assert.Empty(result.CompilationErrors);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"{Ns}.Plugins.DefaultPlugin", generated);
        Assert.DoesNotContain("TracingPlugin", generated);
        Assert.DoesNotContain("AuditingPlugin", generated);
    }

    [Fact]
    public void Decorator_FluentDecorateWithPackageDecorator_DecoratorIsNotAnImplementation()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Time
            {
                public interface IClock { }
                public class SystemClock : IClock { }
                public class CachingClock : IClock
                {
                    public CachingClock(IClock inner) { }
                }
            }
            """);

        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using MyCompany.Contracts.Time;
            using TestAssembly;

            namespace TestAssembly
            {
                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddServicefyConventions().ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Singleton);
                        services.Decorate<IClock, CachingClock>();
                    }
                }
            }
            """;

        var result = PackageCompilationHelper.Run(source, contracts);

        AssertCleanBuild(result);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddKeyedSingleton<{Ns}.Time.IClock, {Ns}.Time.SystemClock>(\"__BASE__\");", generated);
        Assert.Contains($"new {Ns}.Time.CachingClock(", generated);
        Assert.DoesNotContain($"Services.AddSingleton<{Ns}.Time.IClock, {Ns}.Time.CachingClock>();", generated);
        Assert.DoesNotContain($"Services.AddKeyedSingleton<{Ns}.Time.IClock, {Ns}.Time.CachingClock>(\"__BASE__\");", generated);
    }

    [Fact]
    public void ConsumerTypeWithByConfigurationAttribute_UnderMarkerTree_IsExcluded()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            """);

        var source = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public interface IAuditable { }

                    [AddScoped]
                    public class AttributedRepository : IAuditable { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(_ => true, Lifetime.Scoped)");

        var result = PackageCompilationHelper.Run(source, contracts);

        Assert.Empty(result.CompilationErrors);
        var generated = result.ByNamespaceOfSource;
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", generated);
        Assert.DoesNotContain("AttributedRepository", generated);
    }

    [Fact]
    public void PackageAndConsumerImplementations_ResolveAtRuntime()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            namespace MyCompany.Internal.Repositories
            {
                public class HiddenOrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }
            """);

        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using TestAssembly;

            namespace MyCompany.Contracts.Repositories.Sql
            {
                public class SqlOrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }

            namespace TestAssembly.Repositories
            {
                public class OutsideOrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
            }

            namespace TestAssembly
            {
                public static class RepositoryResolver
                {
                    public static string Resolve()
                    {
                        var services = new ServiceCollection();
                        services.AddServicefyConventions()
                            .ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.Contains(".Repositories"), Lifetime.Transient);

                        using var provider = services.BuildServiceProvider();
                        var names = new System.Collections.Generic.List<string>();
                        foreach (var repository in provider.GetServices<MyCompany.Contracts.Repositories.IOrderRepository>())
                            names.Add(repository.GetType().FullName);
                        names.Sort(System.StringComparer.Ordinal);
                        return string.Join(",", names);
                    }
                }
            }
            """;

        var resolved = PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.RepositoryResolver", "Resolve", contracts);

        Assert.Equal(
            "MyCompany.Contracts.Repositories.InMemoryOrderRepository,MyCompany.Contracts.Repositories.Sql.SqlOrderRepository",
            resolved);
    }

    [Fact]
    public void Incremental_EditsKeepAnchoredScanCorrect_AndSwitchingToNonAnchoredMarkerDropsPackageTypes()
    {
        var contracts = Contracts("""
            namespace MyCompany.Contracts { public class ContractsMarker { } }
            namespace MyCompany.Contracts.Repositories
            {
                public interface IOrderRepository { }
                public class InMemoryOrderRepository : IOrderRepository { }
            }
            namespace MyCompany.Contracts.Services
            {
                public interface IPricingService { }
                public class DefaultPricingService : IPricingService { }
            }
            """);

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp13);
        var references = PackageCompilationHelper.BuildReferences(parseOptions, [contracts]);

        const string path = "Consumer.cs";
        const string repositoriesRule = """.ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Repositories"), Lifetime.Scoped)""";

        var initialSource = Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository { }
                }
                """,
            rules: repositoriesRule);

        Compilation compilation = CSharpCompilation.Create(
            assemblyName: PackageCompilationHelper.AssemblyName,
            syntaxTrees: [CSharpSyntaxTree.ParseText(initialSource, parseOptions, path: path, cancellationToken: TestContext.Current.CancellationToken)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new IIncrementalGenerator[] { new ServicefyByConfigurationGenerator(), new ServicefyConventionsGenerator() }
                .Select(g => g.AsSourceGenerator()),
            parseOptions: parseOptions);

        string? ByNamespaceOf(GeneratorDriver d) => d.GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ServicefyConventionsBuilder.ByNamespaceOf.g.cs", StringComparison.Ordinal))
            ?.GetText(TestContext.Current.CancellationToken).ToString();

        Compilation Edit(Compilation c, string newSource)
        {
            var tree = c.SyntaxTrees.Single(t => t.FilePath == path);
            return c.ReplaceSyntaxTree(tree, tree.WithChangedText(SourceText.From(newSource)));
        }

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var first = ByNamespaceOf(driver);
        Assert.NotNull(first);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", first);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", first);

        compilation = Edit(compilation, Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository { }
                    public class CachedOrderRepository : IOrderRepository { }
                }
                namespace TestAssembly.Repositories
                {
                    public class OutsideOrderRepository : MyCompany.Contracts.Repositories.IOrderRepository { }
                }
                """,
            rules: repositoriesRule));
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var second = ByNamespaceOf(driver);
        Assert.NotNull(second);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.InMemoryOrderRepository>();", second);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.SqlOrderRepository>();", second);
        Assert.Contains($"Services.AddScoped<{Repos}.IOrderRepository, {Repos}.CachedOrderRepository>();", second);
        Assert.DoesNotContain("OutsideOrderRepository", second);

        compilation = Edit(compilation, Consumer(
            types: """
                namespace MyCompany.Contracts.Repositories
                {
                    public class SqlOrderRepository : IOrderRepository { }
                }
                """,
            rules: """.ByNamespaceOf<MyCompany.Contracts.ContractsMarker>(ns => ns.EndsWith(".Services"), Lifetime.Singleton)"""));
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var third = ByNamespaceOf(driver);
        Assert.NotNull(third);
        Assert.Contains($"Services.AddSingleton<{Ns}.Services.IPricingService, {Ns}.Services.DefaultPricingService>();", third);
        Assert.DoesNotContain("OrderRepository", third);

        compilation = Edit(compilation, Consumer(
            types: """
                namespace MyCompany.Contracts
                {
                    public class LocalMarker { }
                }
                namespace MyCompany.Contracts.Repositories
                {
                    public interface IAuditable { }
                    public class SqlOrderRepository : IOrderRepository, IAuditable { }
                }
                """,
            rules: ".ByNamespaceOf<MyCompany.Contracts.LocalMarker>(_ => true, Lifetime.Scoped)"));
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var fourth = ByNamespaceOf(driver);
        Assert.NotNull(fourth);
        Assert.Contains($"Services.AddScoped<{Repos}.IAuditable, {Repos}.SqlOrderRepository>();", fourth);
        Assert.DoesNotContain("IOrderRepository", fourth);
        Assert.DoesNotContain("InMemoryOrderRepository", fourth);
        Assert.DoesNotContain("PricingService", fourth);

        compilation = Edit(compilation, initialSource);
        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var fifth = ByNamespaceOf(driver);
        Assert.Equal(first, fifth);
    }
}
