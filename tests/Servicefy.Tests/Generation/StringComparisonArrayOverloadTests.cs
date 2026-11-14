using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class StringComparisonArrayOverloadTests
{
    private const string NamespaceTypes = """
        namespace MyApp.REPOSITORY
        {
            public interface IUpperRepo { }
            public class UpperRepo : IUpperRepo { }
        }

        namespace MyApp.handler
        {
            public interface ILowerHandler { }
            public class LowerHandler : ILowerHandler { }
        }

        namespace MyApp.Handler
        {
            public interface IExactHandler { }
            public class ExactHandler : IExactHandler { }
        }

        namespace MyApp.Other
        {
            public interface IOther { }
            public class Other : IOther { }
        }
        """;

    private const string TypeNameTypes = """
        namespace MyApp.Types
        {
            public interface IOrderStore { }
            public class OrderREPOSITORY : IOrderStore { }

            public interface IUserProcessor { }
            public class Userhandler : IUserProcessor { }

            public interface IPaymentProcessor { }
            public class PaymentHandler : IPaymentProcessor { }

            public interface IClock { }
            public class Clock : IClock { }
        }
        """;

    private const string AllIgnoreCaseNamespace = "IExactHandler,ILowerHandler,IUpperRepo";
    private const string AllIgnoreCaseTypeName = "IOrderStore,IPaymentProcessor,IUserProcessor";

    private static string Consumer(string types, string rule) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using TestAssembly;

        {{types}}

        namespace TestAssembly.Composition
        {
            public static class Acceptance
            {
                public static string Registered()
                {
                    var services = new ServiceCollection();
                    services.AddServicefyConventions()
                        {{rule}};
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var descriptor in services)
                        if (descriptor.ServiceType.Namespace != null && descriptor.ServiceType.Namespace.StartsWith("MyApp", StringComparison.Ordinal))
                            names.Add(descriptor.ServiceType.Name);
                    names.Sort(StringComparer.Ordinal);
                    return string.Join(",", names);
                }
            }
        }
        """;

    private static string RunRegistered(string types, string rule) =>
        Assert.IsType<string>(PackageCompilationHelper.RunStaticMethod(
            Consumer(types, rule), "TestAssembly.Composition.Acceptance", "Registered"));

    private static void AssertCleanBuild(PackageRunResult result)
    {
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationErrors);
    }

    public static TheoryData<string, string> ByNamespaceIgnoreCaseRules => new()
    {
        { """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.EndsWith(new[] { "Repository", "Handler" }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.EndsWith(new string[] { "Repository", "Handler" }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.InvariantCultureIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.CurrentCultureIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.StartsWith(["myapp.repository", "MYAPP.HANDLER"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.StartsWith(new[] { "myapp.repository", "MYAPP.HANDLER" }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.Contains(["repositor", "HANDL"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.Contains(new[] { "repositor", "HANDL" }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.EndsWith(["handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", "IExactHandler,ILowerHandler" },
        { """.ByNamespace(ns => ns.EndsWith(StringComparison.OrdinalIgnoreCase, ["Repository", "Handler"]), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.EndsWith(StringComparison.OrdinalIgnoreCase, new[] { "Repository", "Handler" }), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.EndsWith(StringComparison.OrdinalIgnoreCase, "Repository", "Handler"), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.StartsWith(StringComparison.OrdinalIgnoreCase, "myapp.repository", "MYAPP.HANDLER"), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
        { """.ByNamespace(ns => ns.Contains(StringComparison.OrdinalIgnoreCase, ["repositor", "HANDL"]), Lifetime.Scoped)""", AllIgnoreCaseNamespace },
    };

    public static TheoryData<string, string> ByNamespaceCaseSensitiveRules => new()
    {
        { """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.Ordinal), Lifetime.Scoped)""", "IExactHandler" },
        { """.ByNamespace(ns => ns.EndsWith(new[] { "Repository", "Handler" }, StringComparison.Ordinal), Lifetime.Scoped)""", "IExactHandler" },
        { """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.InvariantCulture), Lifetime.Scoped)""", "IExactHandler" },
        { """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.CurrentCulture), Lifetime.Scoped)""", "IExactHandler" },
        { """.ByNamespace(ns => ns.StartsWith(["MyApp.Repository", "MyApp.Handler"], StringComparison.Ordinal), Lifetime.Scoped)""", "IExactHandler" },
        { """.ByNamespace(ns => ns.Contains(["Repositor", "Handl"], StringComparison.Ordinal), Lifetime.Scoped)""", "IExactHandler" },
        { """.ByNamespace(ns => ns.EndsWith(StringComparison.Ordinal, ["Repository", "Handler"]), Lifetime.Scoped)""", "IExactHandler" },
        { """.ByNamespace(ns => ns.EndsWith(StringComparison.Ordinal, "Repository", "Handler"), Lifetime.Scoped)""", "IExactHandler" },
        { """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"]), Lifetime.Scoped)""", "IExactHandler" },
    };

    public static TheoryData<string, string> ByTypeNameIgnoreCaseRules => new()
    {
        { """.ByTypeName((ns, name) => name.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
        { """.ByTypeName((ns, name) => name.EndsWith(new[] { "Repository", "Handler" }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
        { """.ByTypeName((ns, name) => name.EndsWith(new string[] { "Repository", "Handler" }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
        { """.ByTypeName((ns, name) => name.StartsWith(["orderrep", "USERH", "paymenth"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
        { """.ByTypeName((ns, name) => name.StartsWith(new[] { "orderrep", "USERH", "paymenth" }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
        { """.ByTypeName((ns, name) => name.Contains(["repositor", "HANDL"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
        { """.ByTypeName((ns, name) => name.Contains(new[] { "repositor", "HANDL" }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
        { """.ByTypeName((ns, name) => ns.EndsWith(["types"], StringComparison.OrdinalIgnoreCase) && name.EndsWith(["handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""", "IPaymentProcessor,IUserProcessor" },
        { """.ByTypeName((ns, name) => name.EndsWith(StringComparison.OrdinalIgnoreCase, ["Repository", "Handler"]), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
        { """.ByTypeName((ns, name) => name.EndsWith(StringComparison.OrdinalIgnoreCase, "Repository", "Handler"), Lifetime.Scoped)""", AllIgnoreCaseTypeName },
    };

    public static TheoryData<string, string> ByTypeNameCaseSensitiveRules => new()
    {
        { """.ByTypeName((ns, name) => name.EndsWith(["Repository", "Handler"], StringComparison.Ordinal), Lifetime.Scoped)""", "IPaymentProcessor" },
        { """.ByTypeName((ns, name) => name.EndsWith(new[] { "Repository", "Handler" }, StringComparison.Ordinal), Lifetime.Scoped)""", "IPaymentProcessor" },
        { """.ByTypeName((ns, name) => name.StartsWith(["orderrep", "USERH", "PaymentH"], StringComparison.Ordinal), Lifetime.Scoped)""", "IPaymentProcessor" },
        { """.ByTypeName((ns, name) => name.Contains(["Repositor", "Handl"], StringComparison.Ordinal), Lifetime.Scoped)""", "IPaymentProcessor" },
        { """.ByTypeName((ns, name) => name.EndsWith(StringComparison.Ordinal, ["Repository", "Handler"]), Lifetime.Scoped)""", "IPaymentProcessor" },
        { """.ByTypeName((ns, name) => name.EndsWith(StringComparison.Ordinal, "Repository", "Handler"), Lifetime.Scoped)""", "IPaymentProcessor" },
    };

    [Theory]
    [MemberData(nameof(ByNamespaceIgnoreCaseRules))]
    [MemberData(nameof(ByNamespaceCaseSensitiveRules))]
    public void ByNamespace_ComparisonForms_CompileWithoutErrorsOrDiagnostics(string rule, string expected)
    {
        _ = expected;
        var result = PackageCompilationHelper.Run(Consumer(NamespaceTypes, rule));

        AssertCleanBuild(result);
        Assert.NotNull(result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs"));
    }

    [Theory]
    [MemberData(nameof(ByNamespaceIgnoreCaseRules))]
    [MemberData(nameof(ByNamespaceCaseSensitiveRules))]
    public void ByNamespace_ComparisonForms_RegisterExpectedServicesAtRuntime(string rule, string expected)
    {
        Assert.Equal(expected, RunRegistered(NamespaceTypes, rule));
    }

    [Theory]
    [MemberData(nameof(ByTypeNameIgnoreCaseRules))]
    [MemberData(nameof(ByTypeNameCaseSensitiveRules))]
    public void ByTypeName_ComparisonForms_CompileWithoutErrorsOrDiagnostics(string rule, string expected)
    {
        _ = expected;
        var result = PackageCompilationHelper.Run(Consumer(TypeNameTypes, rule));

        AssertCleanBuild(result);
        Assert.NotNull(result.GeneratedSource("ServicefyConventionsBuilder.ByTypeName.g.cs"));
    }

    [Theory]
    [MemberData(nameof(ByTypeNameIgnoreCaseRules))]
    [MemberData(nameof(ByTypeNameCaseSensitiveRules))]
    public void ByTypeName_ComparisonForms_RegisterExpectedServicesAtRuntime(string rule, string expected)
    {
        Assert.Equal(expected, RunRegistered(TypeNameTypes, rule));
    }

    [Fact]
    public void DocumentedByNamespaceExample_EmitsScopedRegistrationsForCaseInsensitiveMatches()
    {
        var result = PackageCompilationHelper.Run(Consumer(
            NamespaceTypes,
            """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)"""));

        AssertCleanBuild(result);
        var generated = result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("Services.AddScoped<global::MyApp.REPOSITORY.IUpperRepo, global::MyApp.REPOSITORY.UpperRepo>();", generated);
        Assert.Contains("Services.AddScoped<global::MyApp.handler.ILowerHandler, global::MyApp.handler.LowerHandler>();", generated);
        Assert.Contains("Services.AddScoped<global::MyApp.Handler.IExactHandler, global::MyApp.Handler.ExactHandler>();", generated);
        Assert.DoesNotContain("MyApp.Other", generated);
    }

    [Fact]
    public void DocumentedByTypeNameExample_EmitsScopedRegistrationsForCaseInsensitiveMatches()
    {
        var result = PackageCompilationHelper.Run(Consumer(
            TypeNameTypes,
            """.ByTypeName((ns, name) => name.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)"""));

        AssertCleanBuild(result);
        var generated = result.GeneratedSource("ServicefyConventionsBuilder.ByTypeName.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("Services.AddScoped<global::MyApp.Types.IOrderStore, global::MyApp.Types.OrderREPOSITORY>();", generated);
        Assert.Contains("Services.AddScoped<global::MyApp.Types.IUserProcessor, global::MyApp.Types.Userhandler>();", generated);
        Assert.Contains("Services.AddScoped<global::MyApp.Types.IPaymentProcessor, global::MyApp.Types.PaymentHandler>();", generated);
        Assert.DoesNotContain("global::MyApp.Types.Clock", generated);
    }

    [Fact]
    public void DocumentedExamples_BothRulesInOneChain_RegisterUnionAtRuntime()
    {
        var types = NamespaceTypes + Environment.NewLine + TypeNameTypes;
        var rule = """
            .ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)
                        .ByTypeName((ns, name) => name.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)
            """;

        var result = PackageCompilationHelper.Run(Consumer(types, rule));
        AssertCleanBuild(result);

        Assert.Equal(
            "IExactHandler,IExactHandler,ILowerHandler,ILowerHandler,IOrderStore,IPaymentProcessor,IUpperRepo,IUserProcessor",
            RunRegistered(types, rule));
    }

    [Fact]
    public void ByNamespaceOf_ArrayWithTrailingComparison_CompilesAndMatchesCaseInsensitively()
    {
        var types = """
            namespace MyApp.Data
            {
                public class Marker { }
            }

            namespace MyApp.Data.REPOSITORY
            {
                public interface IDataRepo { }
                public class DataRepo : IDataRepo { }
            }

            namespace MyApp.Data.Misc
            {
                public interface IDataMisc { }
                public class DataMisc : IDataMisc { }
            }
            """;
        var rule = """.ByNamespaceOf<MyApp.Data.Marker>(ns => ns.EndsWith(["Repository"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""";

        var result = PackageCompilationHelper.Run(Consumer(types, rule));
        AssertCleanBuild(result);

        Assert.Equal("IDataRepo", RunRegistered(types, rule));
    }

    [Fact]
    public void ByNamespace_ArrayWithTrailingComparison_CombinedWithNegation_MatchesAtRuntime()
    {
        var rule = """.ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase) && !ns.Contains(["handler"], StringComparison.Ordinal), Lifetime.Scoped)""";

        var result = PackageCompilationHelper.Run(Consumer(NamespaceTypes, rule));
        AssertCleanBuild(result);

        Assert.Equal("IExactHandler,IUpperRepo", RunRegistered(NamespaceTypes, rule));
    }

    [Fact]
    public void ByNamespace_ArrayWithTrailingComparison_SameValuesDifferentComparison_AreDistinctRules()
    {
        var rule = """
            .ByNamespace(ns => ns.EndsWith(["Handler"], StringComparison.Ordinal), Lifetime.Singleton)
                        .ByNamespace(ns => ns.EndsWith(["Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Transient)
            """;

        var result = PackageCompilationHelper.Run(Consumer(NamespaceTypes, rule));
        AssertCleanBuild(result);

        var generated = result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs");
        Assert.NotNull(generated);
        Assert.Contains("Services.AddSingleton<global::MyApp.Handler.IExactHandler, global::MyApp.Handler.ExactHandler>();", generated);
        Assert.DoesNotContain("Services.AddSingleton<global::MyApp.handler.ILowerHandler", generated);
        Assert.Contains("Services.AddTransient<global::MyApp.handler.ILowerHandler, global::MyApp.handler.LowerHandler>();", generated);
        Assert.Contains("Services.AddTransient<global::MyApp.Handler.IExactHandler, global::MyApp.Handler.ExactHandler>();", generated);
    }

    [Theory]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(["Repository", Suffix], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(new[] { "Repository", Suffix }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.StartsWith(["MyApp.Repository", Suffix], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.Contains(["Repository", Suffix], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(StringComparison.OrdinalIgnoreCase, ["Repository", Suffix]), Lifetime.Scoped)""")]
    [InlineData(""".ByTypeName((ns, name) => name.EndsWith(["Repository", Suffix], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""")]
    [InlineData(""".ByTypeName((ns, name) => name.EndsWith(new[] { "Repository", Suffix }, StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""")]
    public void NonLiteralArrayElement_WithComparison_ReportsSVCFY011AndIsIgnored(string rule)
    {
        var source = Consumer(NamespaceTypes + Environment.NewLine + TypeNameTypes, rule)
            .Replace("public static string Registered()", "private const string Suffix = \"Handler\";" + Environment.NewLine + "            public static string Registered()");

        var result = PackageCompilationHelper.Run(source);

        Assert.Empty(result.CompilationErrors.Where(d => !d.Id.StartsWith("SVCFY", StringComparison.Ordinal)));
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "SVCFY011");
        Assert.Null(result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs"));
        Assert.Null(result.GeneratedSource("ServicefyConventionsBuilder.ByTypeName.g.cs"));
    }

    [Theory]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], Comparison), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(new[] { "Repository", "Handler" }, Comparison), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(Comparison, ["Repository", "Handler"]), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(Comparison, "Repository", "Handler"), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(["Repository", "Handler"], (StringComparison)5), Lifetime.Scoped)""")]
    [InlineData(""".ByTypeName((ns, name) => name.EndsWith(["Repository", "Handler"], Comparison), Lifetime.Scoped)""")]
    [InlineData(""".ByTypeName((ns, name) => name.StartsWith(new[] { "Order" }, Comparison), Lifetime.Scoped)""")]
    public void NonLiteralComparison_ReportsSVCFY012AndIsIgnored(string rule)
    {
        var source = Consumer(NamespaceTypes + Environment.NewLine + TypeNameTypes, rule)
            .Replace("public static string Registered()", "public static StringComparison Comparison = StringComparison.OrdinalIgnoreCase;" + Environment.NewLine + "            public static string Registered()");

        var result = PackageCompilationHelper.Run(source);

        Assert.Empty(result.CompilationErrors.Where(d => !d.Id.StartsWith("SVCFY", StringComparison.Ordinal)));
        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "SVCFY012");
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Id == "SVCFY011");
        Assert.Null(result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs"));
        Assert.Null(result.GeneratedSource("ServicefyConventionsBuilder.ByTypeName.g.cs"));
    }

    [Theory]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(values: ["Repository", "Handler"], comparison: StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(comparison: StringComparison.OrdinalIgnoreCase, values: ["Repository", "Handler"]), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(ns => ServicefyStringExtensions.EndsWith(ns, ["Repository", "Handler"], StringComparison.OrdinalIgnoreCase), Lifetime.Scoped)""")]
    public void UnusualButValidCallShapes_AreEitherHonouredOrReported_NeverSilentlyMismatched(string rule)
    {
        var result = PackageCompilationHelper.Run(Consumer(NamespaceTypes, rule));

        Assert.Empty(result.CompilationErrors.Where(d => !d.Id.StartsWith("SVCFY", StringComparison.Ordinal)));

        if (result.GeneratorDiagnostics.Any(d => d.Id is "SVCFY011" or "SVCFY012"))
        {
            Assert.Null(result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs"));
            return;
        }

        Assert.Equal(AllIgnoreCaseNamespace, RunRegistered(NamespaceTypes, rule));
    }

    [Fact]
    public void AllReferences_StartsWithArrayIgnoreCase_StillFindsPackageTypesWhenPruning()
    {
        var source = ConventionScopeTestKit.Consumer(
            "",
            """.ByNamespace(ns => ns.StartsWith(["mycompany.shared"], StringComparison.OrdinalIgnoreCase), Lifetime.Singleton, ConventionScope.AllReferences)""");

        var result = PackageCompilationHelper.Run(source, ConventionScopeTestKit.Packages());

        ConventionScopeTestKit.AssertCleanBuild(result);
        var generated = result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs");
        Assert.NotNull(generated);
        Assert.Contains(
            ConventionScopeTestKit.Registration("Singleton", ConventionScopeTestKit.IClock, ConventionScopeTestKit.SystemClock),
            generated);
        Assert.DoesNotContain(ConventionScopeTestKit.IHttpGateway, generated);
    }

    [Fact]
    public void AllReferences_StartsWithArrayOrdinal_DoesNotMatchDifferentlyCasedPackageNamespace()
    {
        var source = ConventionScopeTestKit.Consumer(
            "",
            """.ByNamespace(ns => ns.StartsWith(["mycompany.shared"], StringComparison.Ordinal), Lifetime.Singleton, ConventionScope.AllReferences)""");

        var result = PackageCompilationHelper.Run(source, ConventionScopeTestKit.Packages());

        Assert.Empty(result.CompilationErrors);
        Assert.Null(result.GeneratedSource("ServicefyConventionsBuilder.ByNamespace.g.cs"));
    }

    [Fact]
    public void ByTypeName_AllReferences_ArrayIgnoreCase_MatchesPackageTypes()
    {
        var source = ConventionScopeTestKit.Consumer(
            "",
            """.ByTypeName((ns, name) => ns.StartsWith(["MYCOMPANY.SHARED"], StringComparison.OrdinalIgnoreCase) && name.EndsWith(["clock"], StringComparison.OrdinalIgnoreCase), Lifetime.Singleton, ConventionScope.AllReferences)""");

        var result = PackageCompilationHelper.Run(source, ConventionScopeTestKit.Packages());

        Assert.Empty(result.CompilationErrors);
        Assert.DoesNotContain(result.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var generated = result.GeneratedSource("ServicefyConventionsBuilder.ByTypeName.g.cs");
        Assert.NotNull(generated);
        Assert.Contains(
            ConventionScopeTestKit.Registration("Singleton", ConventionScopeTestKit.IClock, ConventionScopeTestKit.SystemClock),
            generated);
    }

    [Fact]
    public void GeneratedStringExtensions_ExposeAllThreeOverloadShapesForEachMethod()
    {
        var result = PackageCompilationHelper.Run(Consumer(NamespaceTypes, ".ByNamespace(_ => true, Lifetime.Scoped)"));
        Assert.Empty(result.CompilationErrors);

        var extensions = result.Output.GetTypeByMetadataName("TestAssembly.ServicefyStringExtensions");
        Assert.NotNull(extensions);

        foreach (var name in new[] { "StartsWith", "EndsWith", "Contains" })
        {
            var shapes = extensions.GetMembers(name)
                .OfType<IMethodSymbol>()
                .Where(m => m.IsExtensionMethod && m.ReturnType.SpecialType == SpecialType.System_Boolean)
                .Select(m => string.Join(",", m.Parameters.Select(p => (p.IsParams ? "params " : "") + p.Type.ToDisplayString())))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.Contains("string,params string[]", shapes);
            Assert.Contains("string,System.StringComparison,params string[]", shapes);
            Assert.Contains("string,string[],System.StringComparison", shapes);
        }
    }

    [Fact]
    public void GeneratedStringExtensions_BehaveCorrectlyWhenCalledDirectly()
    {
        var source = """
            using System;

            namespace TestAssembly
            {
                public static class Probe
                {
                    public static string Run()
                    {
                        var results = new bool[]
                        {
                            "MyApp.REPOSITORY".EndsWith(new[] { "Repository", "Handler" }, StringComparison.OrdinalIgnoreCase),
                            "MyApp.REPOSITORY".EndsWith(new[] { "Repository", "Handler" }, StringComparison.Ordinal),
                            "MyApp.handler".EndsWith(["Repository", "Handler"], StringComparison.InvariantCultureIgnoreCase),
                            "MyApp.Other".EndsWith(["Repository", "Handler"], StringComparison.OrdinalIgnoreCase),
                            "MYAPP.Services".StartsWith(new[] { "x", "myapp." }, StringComparison.OrdinalIgnoreCase),
                            "MYAPP.Services".StartsWith(new[] { "x", "myapp." }, StringComparison.Ordinal),
                            "MyApp.Data.Repositories".Contains(new[] { "DATA" }, StringComparison.OrdinalIgnoreCase),
                            "MyApp.Data.Repositories".Contains(new[] { "DATA" }, StringComparison.Ordinal),
                            "anything".EndsWith(Array.Empty<string>(), StringComparison.OrdinalIgnoreCase),
                            "anything".StartsWith(new string[0], StringComparison.OrdinalIgnoreCase),
                            "anything".Contains(new string[0], StringComparison.OrdinalIgnoreCase),
                            "MyApp.REPOSITORY".EndsWith(StringComparison.OrdinalIgnoreCase, ["Repository"]),
                            "MyApp.REPOSITORY".EndsWith(StringComparison.OrdinalIgnoreCase, "Repository", "Handler"),
                            "MyApp.REPOSITORY".EndsWith("Repository", "Handler"),
                            "MyApp.Repository".EndsWith(["Repository", "Handler"]),
                            "anything".Contains(new[] { "" }, StringComparison.Ordinal),
                            "Straße".EndsWith(new[] { "STRASSE" }, StringComparison.OrdinalIgnoreCase),
                        };

                        var chars = new char[results.Length];
                        for (var i = 0; i < results.Length; i++)
                            chars[i] = results[i] ? '1' : '0';
                        return new string(chars);
                    }
                }
            }
            """;

        var result = Assert.IsType<string>(PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Probe", "Run"));

        Assert.Equal("10101010000110110", result);
    }

    [Fact]
    public void GeneratedStringExtensions_TrailingComparisonOverload_HonoursEveryComparisonValue()
    {
        var source = """
            using System;

            namespace TestAssembly
            {
                public static class Probe
                {
                    public static string Run()
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        foreach (var c in Enum.GetValues<StringComparison>())
                            parts.Add(c + "=" + ("abcDEF".EndsWith(new[] { "def" }, c) ? "1" : "0")
                                + ("abcDEF".StartsWith(new[] { "ABC" }, c) ? "1" : "0")
                                + ("abcDEF".Contains(new[] { "Cd" }, c) ? "1" : "0"));
                        return string.Join(",", parts);
                    }
                }
            }
            """;

        var result = Assert.IsType<string>(PackageCompilationHelper.RunStaticMethod(source, "TestAssembly.Probe", "Run"));

        Assert.Equal(
            "CurrentCulture=000,CurrentCultureIgnoreCase=111,InvariantCulture=000,InvariantCultureIgnoreCase=111,Ordinal=000,OrdinalIgnoreCase=111",
            result);
    }

    [Fact]
    public void GeneratedXmlDocs_DescribeTrailingComparisonFormForByNamespaceAndByTypeName()
    {
        var result = PackageCompilationHelper.Run(Consumer(NamespaceTypes, ".ByNamespace(_ => true, Lifetime.Scoped)"));

        var builder = result.Output.GetTypeByMetadataName("TestAssembly.IServicefyConventionsBuilder");
        Assert.NotNull(builder);

        var byNamespaceDocs = builder.GetMembers("ByNamespace").Single().GetDocumentationCommentXml();
        var byTypeNameDocs = builder.GetMembers("ByTypeName").Single().GetDocumentationCommentXml();
        var extensionsDocs = result.Output.GetTypeByMetadataName("TestAssembly.ServicefyStringExtensions")!.GetDocumentationCommentXml();

        Assert.Contains("nm.EndsWith([\"Repository\", \"Handler\"], StringComparison.OrdinalIgnoreCase)", byNamespaceDocs);
        Assert.Contains("nm.EndsWith(StringComparison.OrdinalIgnoreCase, \"Repository\", \"Handler\")", byNamespaceDocs);
        Assert.Contains("name.EndsWith([\"Repository\", \"Handler\"], StringComparison.OrdinalIgnoreCase)", byTypeNameDocs);
        Assert.Contains("name.EndsWith([\"Repository\", \"Handler\"], StringComparison.OrdinalIgnoreCase)", extensionsDocs);
        Assert.Contains("name.EndsWith(StringComparison.OrdinalIgnoreCase, \"Repository\", \"Handler\")", extensionsDocs);
    }

    [Fact]
    public void GeneratedXmlDocs_AgreeWithMarkdownDocsThatUnsupportedShapesReportSVCFY012()
    {
        var result = PackageCompilationHelper.Run(Consumer(NamespaceTypes, ".ByNamespace(ns => ns.Length > 3, Lifetime.Scoped)"));

        Assert.Contains(result.GeneratorDiagnostics, d => d.Id == "SVCFY012");

        var builder = result.Output.GetTypeByMetadataName("TestAssembly.IServicefyConventionsBuilder");
        Assert.NotNull(builder);

        foreach (var member in new[] { "ByNamespace", "ByTypeName" })
        {
            var docs = builder.GetMembers(member).Single().GetDocumentationCommentXml();
            Assert.DoesNotContain("no diagnostic", docs);
        }
    }
}
