using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Helpers.ConventionScopeTestKit;

namespace Servicefy.Tests.Generation;

public class ConventionScopeAllReferencesTests
{
    private const string NsFile = ConventionDispatch.ByNamespaceFile;
    private const string TnFile = ConventionDispatch.ByTypeNameFile;

    public static TheoryData<string, bool> ScopeArguments => new()
    {
        { "", false },
        { ", ConventionScope.Compilation", false },
        { ", scope: ConventionScope.Compilation", false },
        { ", ConventionScope.AllReferences", true },
        { ", scope: ConventionScope.AllReferences", true },
    };

    [Theory]
    [MemberData(nameof(ScopeArguments))]
    public void ByNamespace_TypeInTransitivePackage_IsMatchedOnlyWithAllReferences(string scopeArgument, bool matched)
    {
        var source = Consumer(
            types: "",
            rules: $$""".ByNamespace(ns => ns.StartsWith("MyCompany.Shared"), Lifetime.Singleton{{scopeArgument}})""");

        var result = PackageCompilationHelper.Run(source, Packages());

        AssertCleanBuild(result);
        var generated = result.GeneratedSource(NsFile);
        if (!matched)
        {
            Assert.Null(generated);
            return;
        }

        var branch = Assert.Single(ConventionDispatch.Branches(generated));
        Assert.Equal(("Singleton", "AllReferences", """ns => ns.StartsWith("MyCompany.Shared")"""), (branch.Lifetime, branch.Scope, branch.Predicate));
        Assert.Equal([Registration("Singleton", IClock, SystemClock)], branch.Registrations);
    }

    [Theory]
    [MemberData(nameof(ScopeArguments))]
    public void ByTypeName_TypeInTransitivePackage_IsMatchedOnlyWithAllReferences(string scopeArgument, bool matched)
    {
        var source = Consumer(
            types: "",
            rules: $$""".ByTypeName((ns, name) => ns.StartsWith("MyCompany.Shared") && name.EndsWith("Clock"), Lifetime.Singleton{{scopeArgument}})""");

        var result = PackageCompilationHelper.Run(source, Packages());

        AssertCleanBuild(result);
        var generated = result.GeneratedSource(TnFile);
        if (!matched)
        {
            Assert.Null(generated);
            return;
        }

        var branch = Assert.Single(ConventionDispatch.Branches(generated));
        Assert.Equal(("Singleton", "AllReferences"), (branch.Lifetime, branch.Scope));
        Assert.Equal([Registration("Singleton", IClock, SystemClock)], branch.Registrations);
    }

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("MyCompany.Shared")""")]
    [InlineData("ByTypeName", """(ns, name) => ns.StartsWith("MyCompany.Shared") && name.EndsWith("Clock")""")]
    public void TypeInTransitivePackage_ResolvesAtRuntime_OnlyWithAllReferences(string convention, string predicate)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using TestAssembly;

            namespace TestAssembly
            {
                public static class Acceptance
                {
                    public static string Resolve()
                    {
                        var withAll = new ServiceCollection();
                        withAll.AddServicefyConventions().{{convention}}({{predicate}}, Lifetime.Singleton, ConventionScope.AllReferences);

                        var withDefault = new ServiceCollection();
                        withDefault.AddServicefyConventions().{{convention}}({{predicate}}, Lifetime.Singleton);

                        using var all = withAll.BuildServiceProvider();
                        using var byDefault = withDefault.BuildServiceProvider();
                        return Describe(all.GetService<MyCompany.Shared.Clock.IClock>()) + "|" + Describe(byDefault.GetService<MyCompany.Shared.Clock.IClock>());
                    }

                    private static string Describe(MyCompany.Shared.Clock.IClock clock) =>
                        clock is null ? "none" : clock.GetType().FullName + ":" + clock.Name;
                }
            }
            """;

        var resolved = PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Acceptance", "Resolve", Packages());

        Assert.Equal("MyCompany.Shared.Clock.SystemClock:system|none", resolved);
    }

    private const string LocalTypes = """
        namespace MyApp.Services
        {
            public interface IOrderService { }
            public class OrderService : IOrderService { }

            public interface IPricingService { }
            public class PricingService : IPricingService, IDisposable { public void Dispose() { } }
        }
        """;

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("MyApp")""")]
    [InlineData("ByTypeName", """(ns, name) => ns.StartsWith("MyApp") && name.EndsWith("Service")""")]
    public void TwoArgumentCall_GeneratesExactlyWhatExplicitCompilationScopeGenerates(string convention, string predicate)
    {
        var file = convention == "ByNamespace" ? NsFile : TnFile;

        string? Generate(string call)
        {
            var result = PackageCompilationHelper.Run(Consumer(LocalTypes, call), Packages());
            AssertCleanBuild(result);
            return result.GeneratedSource(file);
        }

        var twoArguments = Generate($".{convention}({predicate}, Lifetime.Scoped)");
        var positional = Generate($".{convention}({predicate}, Lifetime.Scoped, ConventionScope.Compilation)");
        var named = Generate($".{convention}({predicate}, Lifetime.Scoped, scope: ConventionScope.Compilation)");

        Assert.NotNull(twoArguments);
        Assert.Equal(twoArguments, positional);
        Assert.Equal(twoArguments, named);

        var branch = Assert.Single(ConventionDispatch.Branches(twoArguments));
        Assert.Equal(("Scoped", "Compilation", predicate), (branch.Lifetime, branch.Scope, branch.Predicate));
        Assert.Equal(
            [
                Registration("Scoped", "MyApp.Services.IOrderService", "MyApp.Services.OrderService"),
                Registration("Scoped", "MyApp.Services.IPricingService", "MyApp.Services.PricingService"),
            ],
            branch.Registrations);
    }

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("MyApp")""")]
    [InlineData("ByTypeName", """(ns, name) => ns.StartsWith("MyApp")""")]
    public void TwoArgumentCall_AndExplicitCompilationScope_ShareOneBranch(string convention, string predicate)
    {
        var source = Consumer(
            LocalTypes,
            $"""
            .{convention}({predicate}, Lifetime.Scoped)
                        .{convention}({predicate}, Lifetime.Scoped, ConventionScope.Compilation)
            """);

        var result = PackageCompilationHelper.Run(source, Packages());

        AssertCleanBuild(result);
        var branch = Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(convention == "ByNamespace" ? NsFile : TnFile)));
        Assert.Equal("Compilation", branch.Scope);
    }

    [Fact]
    public void CompilationScope_StillScansProjectReferences_ButNeverPackages()
    {
        var project = ReferencedAssembly.Project("MyCompany.Features", """
            namespace MyCompany.Features.Billing
            {
                public interface IBillingService { }
                public class BillingService : IBillingService { }
            }
            """);

        var source = Consumer(types: "", rules: """.ByNamespace(ns => ns.StartsWith("MyCompany."), Lifetime.Scoped)""");

        var result = PackageCompilationHelper.Run(source, [.. Packages(), project]);

        AssertCleanBuild(result);
        var branch = Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(NsFile)));
        Assert.Equal(
            [Registration("Scoped", "MyCompany.Features.Billing.IBillingService", "MyCompany.Features.Billing.BillingService")],
            branch.Registrations);
    }

    [Fact]
    public void ConventionScopeEnum_IsGenerated_AndBothConventionsDefaultToCompilation()
    {
        var result = PackageCompilationHelper.Run(Consumer(types: "", rules: ""), Packages());

        AssertCleanBuild(result);
        var scopeEnum = result.GeneratedSource("ConventionScope.g.cs");
        Assert.NotNull(scopeEnum);
        Assert.Contains("enum ConventionScope", scopeEnum);
        Assert.Contains("Compilation = 0", scopeEnum);
        Assert.Contains("AllReferences", scopeEnum);

        var builder = result.GeneratedSource("ServicefyConventionsBuilder.g.cs");
        Assert.NotNull(builder);
        Assert.Matches(@"ByNamespace\(\s*Func<string, bool> predicate,\s*Lifetime lifetime,\s*ConventionScope scope = ConventionScope\.Compilation,", builder);
        Assert.Matches(@"ByTypeName\(\s*Func<string, string, bool> predicate,\s*Lifetime lifetime,\s*ConventionScope scope = ConventionScope\.Compilation,", builder);
    }

    public static TheoryData<string, string> UnevaluableScopes => new()
    {
        { "var scope = ConventionScope.AllReferences;", "scope" },
        { "", "(ConventionScope)1" },
        { "", "TestAssembly.ConventionScope.AllReferences" },
        { "", "global::TestAssembly.ConventionScope.AllReferences" },
    };

    [Theory]
    [MemberData(nameof(UnevaluableScopes))]
    public void UnevaluableScopeArgument_CallIsIgnored_AndTheProjectStillCompiles(string setup, string scopeArgument)
    {
        foreach (var convention in new[] { ("ByNamespace", """ns => ns.StartsWith("MyApp")""", NsFile), ("ByTypeName", """(ns, name) => ns.StartsWith("MyApp")""", TnFile) })
        {
            var source = $$"""
                using System;
                using Microsoft.Extensions.DependencyInjection;
                using TestAssembly;

                {{LocalTypes}}

                namespace TestAssembly.Composition
                {
                    public static class Startup
                    {
                        public static void Configure(IServiceCollection services)
                        {
                            {{setup}}
                            services.AddServicefyConventions().{{convention.Item1}}({{convention.Item2}}, Lifetime.Scoped, {{scopeArgument}});
                        }
                    }
                }
                """;

            var result = PackageCompilationHelper.Run(source, Packages());

            Assert.Empty(result.CompilationErrors);
            Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            Assert.Null(result.GeneratedSource(convention.Item3));
        }
    }

    public static TheoryData<string, bool, bool> ReferencePaths => new()
    {
        { "/home/test/.nuget/packages/vendor.probe/1.0.0/lib/net8.0/Probe.Lib.dll", true, false },
        { "/home/test/.nuget/packages/vendor.probe/2.1.0/lib/netstandard2.0/Probe.Lib.dll", true, false },
        { @"C:\Users\dev\.nuget\packages\vendor.probe\1.0.0\lib\net8.0\Probe.Lib.dll", true, false },
        { "/Users/dev/.NuGet/Packages/vendor.probe/1.0.0/lib/net8.0/Probe.Lib.dll", true, false },
        { "/src/Probe.Lib/bin/Debug/net8.0/Probe.Lib.dll", true, true },
        { @"C:\src\Probe.Lib\bin\Release\net8.0\Probe.Lib.dll", true, true },
        { "/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.0/Probe.Lib.dll", false, false },
        { "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.0/ref/net8.0/Probe.Lib.dll", false, false },
        { @"C:\Program Files\dotnet\packs\Microsoft.AspNetCore.App.Ref\8.0.0\ref\net8.0\Probe.Lib.dll", false, false },
        { "/home/test/.nuget/packages/microsoft.netcore.app.ref/9.0.0/ref/net9.0/Probe.Lib.dll", false, false },
        { "/home/test/.nuget/packages/microsoft.aspnetcore.app.ref/8.0.0/ref/net8.0/Probe.Lib.dll", false, false },
        { "/home/test/.nuget/packages/netstandard.library/2.0.3/build/netstandard2.0/ref/Probe.Lib.dll", false, false },
        { "/home/test/.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2/Probe.Lib.dll", false, false },
        { @"C:\Users\dev\.nuget\packages\Microsoft.NETCore.App.Ref\8.0.0\ref\net8.0\Probe.Lib.dll", false, false },
    };

    [Theory]
    [MemberData(nameof(ReferencePaths))]
    public void ReferencedAssembly_IsScannedAccordingToWhereItResolvesFrom(string filePath, bool scannedByAllReferences, bool scannedByCompilation)
    {
        var probe = new ReferencedAssembly("Probe.Lib", """
            namespace MyCompany.Probe
            {
                public interface IProbeService { }
                public class ProbeService : IProbeService { }
            }
            """, FilePath: filePath);

        const string types = """
            namespace MyCompany.App
            {
                public class LocalProbe : MyCompany.Probe.IProbeService { }
            }
            """;

        foreach (var (scope, scanned) in new[] { ("ConventionScope.AllReferences", scannedByAllReferences), ("ConventionScope.Compilation", scannedByCompilation) })
        {
            var source = Consumer(types, $$""".ByNamespace(ns => ns.StartsWith("MyCompany."), Lifetime.Scoped, {{scope}})""");

            var result = PackageCompilationHelper.Run(source, probe);

            AssertCleanBuild(result);
            var generated = result.GeneratedSource(NsFile);
            if (!scanned)
            {
                Assert.Null(generated);
                continue;
            }

            var branch = Assert.Single(ConventionDispatch.Branches(generated));
            Assert.Equal(
                [
                    Registration("Scoped", "MyCompany.Probe.IProbeService", "MyCompany.App.LocalProbe"),
                    Registration("Scoped", "MyCompany.Probe.IProbeService", "MyCompany.Probe.ProbeService"),
                ],
                branch.Registrations);
        }
    }

    [Fact]
    public void AllReferences_DoesNotScanTheBcl_EvenWhenThePredicateMatchesItsNamespaces()
    {
        var source = Consumer(
            types: """
                namespace System.Local
                {
                    public interface ILocalService { }
                    public class LocalService : ILocalService { }
                }
                """,
            rules: """.ByNamespace(ns => ns.StartsWith("System"), Lifetime.Scoped, ConventionScope.AllReferences)""");

        var result = PackageCompilationHelper.Run(source, Packages());

        AssertCleanBuild(result);
        var branch = Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(NsFile)));
        Assert.Equal([Registration("Scoped", "System.Local.ILocalService", "System.Local.LocalService")], branch.Registrations);
    }

    [Fact]
    public void ProjectTypeImplementingPackageInterface_IsRegisteredAgainstIt_OnlyByTheAllReferencesRule()
    {
        const string predicate = """ns => ns.StartsWith("MyApp")""";
        var source = Consumer(
            types: """
                namespace MyApp.Clocks
                {
                    public interface IAppClock { }

                    public class UtcClock : MyCompany.Shared.Clock.IClock, IAppClock, IDisposable
                    {
                        public string Name => "utc";
                        public void Dispose() { }
                    }

                    // Implements nothing but the package interface.
                    public class LocalClock : MyCompany.Shared.Clock.IClock { public string Name => "local"; }
                }
                """,
            rules: $"""
                .ByNamespace({predicate}, Lifetime.Scoped)
                            .ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)
                """);

        var result = PackageCompilationHelper.Run(source, Packages());

        AssertCleanBuild(result);
        var generated = result.GeneratedSource(NsFile);

        var compilationBranch = ConventionDispatch.Branch(generated, "Scoped", "Compilation", predicate);
        Assert.NotNull(compilationBranch);
        Assert.Equal([Registration("Scoped", "MyApp.Clocks.IAppClock", "MyApp.Clocks.UtcClock")], compilationBranch.Registrations);

        var allBranch = ConventionDispatch.Branch(generated, "Scoped", "AllReferences", predicate);
        Assert.NotNull(allBranch);
        Assert.Equal(
            [
                Registration("Scoped", "MyApp.Clocks.IAppClock", "MyApp.Clocks.UtcClock"),
                Registration("Scoped", IClock, "MyApp.Clocks.LocalClock"),
                Registration("Scoped", IClock, "MyApp.Clocks.UtcClock"),
            ],
            allBranch.Registrations);

        Assert.DoesNotContain("IDisposable", generated);
    }

    [Fact]
    public void PackageType_IsRegisteredAgainstInterfacesOfAnyPackage_ButNotAgainstBclInterfaces()
    {
        var packages = Packages(sdkExtra: """
            namespace MyCompany.Sdk.Time
            {
                // Interface of the transitive package.
                public class NetworkClock : MyCompany.Shared.Clock.IClock { public string Name => "network"; }

                // Interface of the MS.DI abstractions package.
                public class NullScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
                {
                    public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => null;
                }

                // Only a BCL interface: nothing to register.
                public class Stopwatch : IDisposable { public void Dispose() { } }
            }
            """);

        var source = Consumer(types: "", rules: """.ByNamespace(ns => ns.StartsWith("MyCompany.Sdk"), Lifetime.Singleton, ConventionScope.AllReferences)""");

        var result = PackageCompilationHelper.Run(source, packages);

        AssertCleanBuild(result);
        var branch = Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(NsFile)));
        Assert.Equal(
            [
                Registration("Singleton", "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory", "MyCompany.Sdk.Time.NullScopeFactory"),
                Registration("Singleton", IHttpGateway, HttpGateway),
                Registration("Singleton", IClock, "MyCompany.Sdk.Time.NetworkClock"),
            ],
            branch.Registrations);
    }

    [Fact]
    public void ProjectReferenceTypeImplementingPackageInterface_IsRegisteredAgainstIt_OnlyWithAllReferences()
    {
        var project = ReferencedAssembly.Project("MyCompany.Features", """
            namespace MyCompany.Features.Clocks
            {
                public interface IFeatureClock { }
                public class FeatureClock : MyCompany.Shared.Clock.IClock, IFeatureClock { public string Name => "feature"; }
            }
            """);

        const string predicate = """ns => ns.StartsWith("MyCompany.Features")""";
        var source = Consumer(
            types: "",
            rules: $"""
                .ByNamespace({predicate}, Lifetime.Transient)
                            .ByNamespace({predicate}, Lifetime.Transient, ConventionScope.AllReferences)
                """);

        var result = PackageCompilationHelper.Run(source, [.. Packages(), project]);

        AssertCleanBuild(result);
        var generated = result.GeneratedSource(NsFile);
        Assert.Equal(
            [Registration("Transient", "MyCompany.Features.Clocks.IFeatureClock", "MyCompany.Features.Clocks.FeatureClock")],
            ConventionDispatch.Branch(generated, "Transient", "Compilation", predicate)?.Registrations);
        Assert.Equal(
            [
                Registration("Transient", "MyCompany.Features.Clocks.IFeatureClock", "MyCompany.Features.Clocks.FeatureClock"),
                Registration("Transient", IClock, "MyCompany.Features.Clocks.FeatureClock"),
            ],
            ConventionDispatch.Branch(generated, "Transient", "AllReferences", predicate)?.Registrations);
    }

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("Vendor.Plugins")""")]
    [InlineData("ByTypeName", """(ns, name) => ns.StartsWith("Vendor.Plugins")""")]
    public void PackageCandidates_OnlyPublicConcreteNonGenericClasses_AreRegistered(string convention, string predicate)
    {
        var plugins = ReferencedAssembly.Package("Vendor.Plugins", """
            using System;

            namespace Vendor.Plugins
            {
                public interface IPlugin { }

                public class DefaultPlugin : IPlugin { }
                public sealed class SealedPlugin : IPlugin { }
                public record RecordPlugin : IPlugin;

                internal class InternalPlugin : IPlugin { }
                public abstract class AbstractPlugin : IPlugin { }
                public class GenericPlugin<T> : IPlugin { }
                public struct StructPlugin : IPlugin { }
                public class NoInterfacePlugin { }
                public class DisposableOnly : IDisposable { public void Dispose() { } }

                public class Outer
                {
                    public class NestedPlugin : IPlugin { }
                    internal class InternalNestedPlugin : IPlugin { }
                }

                internal class InternalOuter
                {
                    public class NestedInInternalPlugin : IPlugin { }
                }

                public static class StaticHolder { }
            }
            """);

        var source = Consumer(types: "", rules: $".{convention}({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)");

        var result = PackageCompilationHelper.Run(source, plugins);

        AssertCleanBuild(result);
        var branch = Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(convention == "ByNamespace" ? NsFile : TnFile)));
        Assert.Equal(
            [
                Registration("Scoped", "Vendor.Plugins.IPlugin", "Vendor.Plugins.DefaultPlugin"),
                Registration("Scoped", "Vendor.Plugins.IPlugin", "Vendor.Plugins.Outer.NestedPlugin"),
                Registration("Scoped", "Vendor.Plugins.IPlugin", "Vendor.Plugins.RecordPlugin"),
                Registration("Scoped", "Vendor.Plugins.IPlugin", "Vendor.Plugins.SealedPlugin"),
            ],
            branch.Registrations);
    }

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("MyCompany.Shared")""")]
    [InlineData("ByTypeName", """(ns, name) => ns.StartsWith("MyCompany.Shared")""")]
    public void DecoratorsShippedInPackage_AreNotRegisteredAsImplementations(string convention, string predicate)
    {
        var packages = Packages(sharedExtra: """
            namespace MyCompany.Shared
            {
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                public sealed class DecoratorForAttribute<TService> : Attribute { }

                [AttributeUsage(AttributeTargets.Class, Inherited = false)]
                public sealed class DecoratorAttribute : Attribute { }
            }

            namespace MyCompany.Shared.Clock
            {
                [MyCompany.Shared.DecoratorFor<IClock>]
                public class TracingClock : IClock
                {
                    public TracingClock(IClock inner) { }
                    public string Name => "tracing";
                }

                [MyCompany.Shared.Decorator]
                public class AuditingClock : IClock
                {
                    public AuditingClock(IClock inner) { }
                    public string Name => "auditing";
                }
            }
            """);

        var source = Consumer(types: "", rules: $".{convention}({predicate}, Lifetime.Singleton, ConventionScope.AllReferences)");

        var result = PackageCompilationHelper.Run(source, packages);

        Assert.Empty(result.CompilationErrors);
        var generated = result.GeneratedSource(convention == "ByNamespace" ? NsFile : TnFile);
        var branch = Assert.Single(ConventionDispatch.Branches(generated));
        Assert.Equal([Registration("Singleton", IClock, SystemClock)], branch.Registrations);
        Assert.DoesNotContain("TracingClock", generated);
        Assert.DoesNotContain("AuditingClock", generated);
    }

    private const string CachingClockPackageSource = """
        namespace MyCompany.Shared.Clock
        {
            public class CachingClock : IClock
            {
                private readonly IClock _inner;
                public CachingClock(IClock inner) { _inner = inner; }
                public string Name => "caching(" + _inner.Name + ")";
            }
        }
        """;

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("MyCompany.Shared")""")]
    [InlineData("ByTypeName", """(ns, name) => ns.StartsWith("MyCompany.Shared")""")]
    public void PackageDecoratorWiredWithDecorate_WrapsThePackageImplementation_AndIsNotAnImplementation(string convention, string predicate)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using MyCompany.Shared.Clock;
            using TestAssembly;

            namespace TestAssembly
            {
                public static class Clocks
                {
                    public static string Resolve()
                    {
                        var services = new ServiceCollection();
                        services.AddServicefyConventions().{{convention}}({{predicate}}, Lifetime.Singleton, ConventionScope.AllReferences);
                        services.Decorate<IClock, CachingClock>();

                        using var provider = services.BuildServiceProvider();
                        var names = new System.Collections.Generic.List<string>();
                        foreach (var clock in provider.GetServices<IClock>())
                            names.Add(clock.Name);
                        return string.Join(",", names);
                    }
                }
            }
            """;

        var packages = Packages(sharedExtra: CachingClockPackageSource);

        var result = PackageCompilationHelper.Run(
            Consumer(
                types: "",
                rules: $".{convention}({predicate}, Lifetime.Singleton, ConventionScope.AllReferences);\n" +
                       "            services.Decorate<MyCompany.Shared.Clock.IClock, MyCompany.Shared.Clock.CachingClock>()"),
            packages);
        AssertCleanBuild(result);
        var generated = result.GeneratedSource(convention == "ByNamespace" ? NsFile : TnFile);
        Assert.NotNull(generated);
        Assert.Contains($"Services.AddKeyedSingleton<global::{IClock}, global::{SystemClock}>(\"__BASE__\");", generated);
        Assert.Contains($"new global::MyCompany.Shared.Clock.CachingClock(", generated);
        Assert.DoesNotContain($"<global::{IClock}, global::MyCompany.Shared.Clock.CachingClock>", generated);

        var resolved = PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Clocks", "Resolve", packages);
        Assert.Equal("caching(system)", resolved);
    }

    [Fact]
    public void ProjectTypesWithByConfigurationAttribute_AreExcluded_UnderAllReferences()
    {
        var source = Consumer(
            types: """
                namespace MyApp.Clocks
                {
                    [AddSingleton]
                    public class AttributedClock : MyCompany.Shared.Clock.IClock { public string Name => "attributed"; }

                    [AddKeyedSingleton("utc")]
                    public class KeyedClock : MyCompany.Shared.Clock.IClock { public string Name => "keyed"; }

                    public class PlainClock : MyCompany.Shared.Clock.IClock { public string Name => "plain"; }
                }
                """,
            rules: """
                .ByNamespace(ns => ns.StartsWith("MyApp"), Lifetime.Singleton, ConventionScope.AllReferences)
                            .ByTypeName((ns, name) => name.EndsWith("Clock"), Lifetime.Singleton, ConventionScope.AllReferences)
                """);

        var result = PackageCompilationHelper.Run(source, Packages());

        Assert.Empty(result.CompilationErrors);
        Assert.Equal(
            [Registration("Singleton", IClock, "MyApp.Clocks.PlainClock")],
            Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(NsFile))).Registrations);
        Assert.Equal(
            [Registration("Singleton", IClock, "MyApp.Clocks.PlainClock"), Registration("Singleton", IClock, SystemClock)],
            Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(TnFile))).Registrations);
    }

    private const string DispatchTypes = """
        namespace MyCompany.Shared.Clock
        {
            // Same namespace as the package's SystemClock; ILocalClock is the project's own interface.
            public interface ILocalClock { }
            public class LocalClock : IClock, ILocalClock { public string Name => "local"; }
        }
        """;

    private static string[] CompilationRegistrations(string lifetime) =>
        [Registration(lifetime, "MyCompany.Shared.Clock.ILocalClock", "MyCompany.Shared.Clock.LocalClock")];

    private static string[] AllReferencesRegistrations(string lifetime) =>
    [
        Registration(lifetime, IClock, "MyCompany.Shared.Clock.LocalClock"),
        Registration(lifetime, IClock, SystemClock),
        Registration(lifetime, "MyCompany.Shared.Clock.ILocalClock", "MyCompany.Shared.Clock.LocalClock"),
    ];

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("MyCompany.Shared")""")]
    [InlineData("ByTypeName", """(ns, name) => name.EndsWith("Clock")""")]
    public void SamePredicateAndLifetime_BothScopes_OneBranchPerScope(string convention, string predicate)
    {
        var source = Consumer(
            DispatchTypes,
            $"""
            .{convention}({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)
                        .{convention}({predicate}, Lifetime.Scoped)
            """);

        var result = PackageCompilationHelper.Run(source, Packages());

        AssertCleanBuild(result);
        var generated = result.GeneratedSource(convention == "ByNamespace" ? NsFile : TnFile);
        Assert.Equal(2, ConventionDispatch.Branches(generated).Count);
        Assert.Equal(CompilationRegistrations("Scoped"), ConventionDispatch.Branch(generated, "Scoped", "Compilation", predicate)?.Registrations);
        Assert.Equal(AllReferencesRegistrations("Scoped"), ConventionDispatch.Branch(generated, "Scoped", "AllReferences", predicate)?.Registrations);
    }

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("MyCompany.Shared")""")]
    [InlineData("ByTypeName", """(ns, name) => name.EndsWith("Clock")""")]
    public void DifferentLifetimesAndScopes_EachRuleGetsItsOwnBranch(string convention, string predicate)
    {
        var source = Consumer(
            DispatchTypes,
            $"""
            .{convention}({predicate}, Lifetime.Singleton, ConventionScope.AllReferences)
                        .{convention}({predicate}, Lifetime.Transient)
                        .{convention}({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)
                        .{convention}({predicate}, Lifetime.Scoped, ConventionScope.Compilation)
                        .{convention}({predicate}, Lifetime.Singleton, scope: ConventionScope.AllReferences)
            """);

        var result = PackageCompilationHelper.Run(source, Packages());

        AssertCleanBuild(result);
        var branches = ConventionDispatch.Branches(result.GeneratedSource(convention == "ByNamespace" ? NsFile : TnFile));

        Assert.Equal(
            ["Scoped/AllReferences", "Scoped/Compilation", "Singleton/AllReferences", "Transient/Compilation"],
            branches.Select(b => $"{b.Lifetime}/{b.Scope}").OrderBy(s => s, StringComparer.Ordinal));

        foreach (var branch in branches)
        {
            var expected = branch.Scope == "AllReferences"
                ? AllReferencesRegistrations(branch.Lifetime)
                : CompilationRegistrations(branch.Lifetime);
            Assert.Equal(expected, branch.Registrations);
        }
    }

    [Theory]
    [InlineData("ByNamespace", """ns => ns.StartsWith("MyCompany.Shared")""")]
    [InlineData("ByTypeName", """(ns, name) => name.EndsWith("Clock")""")]
    public void BothScopes_DispatchAtRuntime_ToTheirOwnRegistrations(string convention, string predicate)
    {
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using TestAssembly;

            {{DispatchTypes}}

            namespace TestAssembly
            {
                public static class Dispatch
                {
                    public static string Resolve()
                    {
                        var compilation = new ServiceCollection();
                        compilation.AddServicefyConventions().{{convention}}({{predicate}}, Lifetime.Transient);

                        var explicitCompilation = new ServiceCollection();
                        explicitCompilation.AddServicefyConventions().{{convention}}({{predicate}}, Lifetime.Transient, ConventionScope.Compilation);

                        var all = new ServiceCollection();
                        all.AddServicefyConventions().{{convention}}({{predicate}}, Lifetime.Singleton, ConventionScope.AllReferences);

                        // Same lifetime as the Compilation rule, but no rule was declared with it: nothing.
                        var undeclared = new ServiceCollection();
                        undeclared.AddServicefyConventions().{{convention}}({{predicate}}, Lifetime.Transient, (ConventionScope)99);

                        return Describe(compilation) + "|" + Describe(explicitCompilation) + "|" + Describe(all) + "|" + Describe(undeclared);
                    }

                    private static string Describe(IServiceCollection services)
                    {
                        var registrations = new List<string>();
                        foreach (var descriptor in services)
                            registrations.Add(descriptor.ServiceType.Name + "/" + descriptor.ImplementationType.Name + ":" + descriptor.Lifetime);
                        registrations.Sort(System.StringComparer.Ordinal);
                        return string.Join(",", registrations);
                    }
                }
            }
            """;

        var resolved = PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Dispatch", "Resolve", Packages());

        Assert.Equal(
            "ILocalClock/LocalClock:Transient" +
            "|ILocalClock/LocalClock:Transient" +
            "|IClock/LocalClock:Singleton,IClock/SystemClock:Singleton,ILocalClock/LocalClock:Singleton" +
            "|",
            resolved);
    }

    [Fact]
    public void AllReferencesRule_DoesNotWidenOtherConventions()
    {
        var source = Consumer(
            DispatchTypes,
            """
            .ByNamespace(ns => ns.StartsWith("MyCompany.Shared"), Lifetime.Scoped, ConventionScope.AllReferences)
                        .ByTypeName((ns, name) => name.EndsWith("Clock"), Lifetime.Scoped)
                        .ByNamespaceOf<MyCompany.Shared.Clock.LocalClock>(_ => true, Lifetime.Scoped)
            """);

        var result = PackageCompilationHelper.Run(source, Packages());

        Assert.Empty(result.CompilationErrors);
        Assert.Equal(AllReferencesRegistrations("Scoped"), Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(NsFile))).Registrations);
        Assert.Equal(CompilationRegistrations("Scoped"), Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(TnFile))).Registrations);

        var byNamespaceOf = result.ByNamespaceOfSource;
        Assert.NotNull(byNamespaceOf);
        Assert.Contains("Services.AddScoped<global::MyCompany.Shared.Clock.ILocalClock, global::MyCompany.Shared.Clock.LocalClock>();", byNamespaceOf);
        Assert.DoesNotContain($"<global::{IClock},", byNamespaceOf);
        Assert.DoesNotContain("SystemClock", byNamespaceOf);
    }

    [Fact]
    public void AllReferencesRuleMatchingNothing_EmitsNoBranch_OtherRulesUnaffected()
    {
        var source = Consumer(
            LocalTypes,
            """
            .ByNamespace(ns => ns.StartsWith("Nowhere.AtAll"), Lifetime.Scoped, ConventionScope.AllReferences)
                        .ByNamespace(ns => ns.StartsWith("MyApp"), Lifetime.Scoped)
            """);

        var result = PackageCompilationHelper.Run(source, Packages());

        AssertCleanBuild(result);
        var branch = Assert.Single(ConventionDispatch.Branches(result.GeneratedSource(NsFile)));
        Assert.Equal(("Compilation", """ns => ns.StartsWith("MyApp")"""), (branch.Scope, branch.Predicate));
    }
}
