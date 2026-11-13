using Microsoft.CodeAnalysis;
using Servicefy.Package;
using Servicefy.Tests.Helpers;
using static Servicefy.Tests.Helpers.ConventionScopeTestKit;

namespace Servicefy.Tests.Generation;

public class ConventionScopePruningTests
{
    private const string PlatformName = "Acme.Platform";
    private const string ExtrasName = "Acme.Extras";

    private static readonly Dictionary<string, string> Implementations = new(StringComparer.Ordinal)
    {
        ["GlobalService"] = "",
        ["RootService"] = "MyCompany",
        ["DataService"] = "MyCompany.Data",
        ["SqlService"] = "MyCompany.Data.Sql",
        ["DataAccessService"] = "MyCompany.DataAccess",
        ["OtherService"] = "MyCompany.Other",
        ["ToolService"] = "Vendor.Tools",
        ["ToolboxService"] = "Vendor.Toolbox",
        ["DeepService"] = "Unrelated.Deep.Inside",
        ["ExtraService"] = "MyCompany.Data.Extras",
        ["ToolExtraService"] = "Vendor.Tools.Extras",
        ["ElsewhereService"] = "Elsewhere",
        ["LocalDataService"] = "MyCompany.Data.Local",
        ["LocalOtherService"] = "Local.Other",
    };

    private static string Declare(params string[] names) => string.Join("\n", names.Select(name =>
    {
        var ns = Implementations[name];
        var types = $"public interface I{name} {{ }} public class {name} : I{name} {{ }}";
        return ns.Length == 0 ? types : $"namespace {ns} {{ {types} }}";
    }));

    private static ReferencedAssembly[] PackageGraph() =>
    [
        ReferencedAssembly.Package(PlatformName, Declare(
            "GlobalService", "RootService", "DataService", "SqlService", "DataAccessService", "OtherService",
            "ToolService", "ToolboxService", "DeepService")),
        ReferencedAssembly.Package(ExtrasName, Declare("ExtraService", "ToolExtraService", "ElsewhereService")),
    ];

    private static string ProjectTypes => Declare("LocalDataService", "LocalOtherService");

    private static string ExpectedRegistration(string name)
    {
        var ns = Implementations[name];
        var prefix = ns.Length == 0 ? "" : ns + ".";
        return Registration("Scoped", $"{prefix}I{name}", prefix + name);
    }

    private sealed record Run(PackageRunResult Result, IAssemblySymbol Platform, IAssemblySymbol Extras)
    {
        public IReadOnlyList<string> Registrations(string file) =>
            ConventionDispatch.Branches(Result.GeneratedSource(file)).SingleOrDefault()?.Registrations ?? [];
    }

    private static Run RunRule(string rule)
    {
        var compilation = PackageCompilationHelper.CreateCompilation(Consumer(ProjectTypes, rule), PackageGraph());
        var result = PackageCompilationHelper.Run(compilation);

        Assert.Empty(result.CompilationErrors);
        return new Run(
            result,
            TypeCollectorProbe.ReferencedAssembly(compilation, PlatformName),
            TypeCollectorProbe.ReferencedAssembly(compilation, ExtrasName));
    }

    private static string Unprunable(string predicate)
    {
        var arrow = predicate.IndexOf("=>", StringComparison.Ordinal);
        return $"{predicate[..arrow]}=> !(!({predicate[(arrow + 2)..].Trim()}))";
    }

    public static TheoryData<string, string, bool, string> PrunablePredicates => new()
    {
        { """ns => ns.StartsWith("MyCompany.Data")""", "MyCompany.Data", false, "DataService|SqlService|DataAccessService|ExtraService|LocalDataService" },
        { """ns => ns.StartsWith("MyCompany.Data.")""", "MyCompany.Data.", false, "SqlService|ExtraService|LocalDataService" },
        { """ns => ns.StartsWith("MyCompany.Data", StringComparison.Ordinal)""", "MyCompany.Data", false, "DataService|SqlService|DataAccessService|ExtraService|LocalDataService" },
        { """ns => ns.StartsWith("mycompany.data", StringComparison.OrdinalIgnoreCase)""", "mycompany.data", true, "DataService|SqlService|DataAccessService|ExtraService|LocalDataService" },
        { """ns => ns.StartsWith("mycompany.data")""", "mycompany.data", false, "" },
        { """ns => ns.StartsWith("MyCompany.Data", "Vendor.Tools")""", "MyCompany.Data|Vendor.Tools", false, "DataService|SqlService|DataAccessService|ExtraService|LocalDataService|ToolService|ToolExtraService" },
        { """ns => ns.StartsWith(StringComparison.OrdinalIgnoreCase, "MYCOMPANY.DATA", "vendor.tool")""", "MYCOMPANY.DATA|vendor.tool", true, "DataService|SqlService|DataAccessService|ExtraService|LocalDataService|ToolService|ToolboxService|ToolExtraService" },
        { """ns => ns.StartsWith(["MyCompany.Data", "Vendor.Tools"])""", "MyCompany.Data|Vendor.Tools", false, "DataService|SqlService|DataAccessService|ExtraService|LocalDataService|ToolService|ToolExtraService" },
        { """ns => ns.StartsWith(StringComparison.OrdinalIgnoreCase, "vendor.tools")""", "vendor.tools", true, "ToolService|ToolExtraService" },
        { """ns => ns == "MyCompany.Data" """, "MyCompany.Data", false, "DataService" },
        { """ns => "Vendor.Tools" == ns""", "Vendor.Tools", false, "ToolService" },
        { """ns => ns.Equals("MyCompany.Data.Sql")""", "MyCompany.Data.Sql", false, "SqlService" },
        { """ns => ns.Equals("mycompany.data.sql", StringComparison.OrdinalIgnoreCase)""", "mycompany.data.sql", true, "SqlService" },
        { """ns => ns.StartsWith("MyCompany") && ns.EndsWith(".Data")""", "MyCompany", false, "DataService" },
        { """ns => ns.EndsWith("Sql") && ns.StartsWith("MyCompany.Data")""", "MyCompany.Data", false, "SqlService" },
        { """ns => ns.StartsWith("MyCompany.Data") && !ns.Contains("Sql")""", "MyCompany.Data", false, "DataService|DataAccessService|ExtraService|LocalDataService" },
        { """ns => ns.StartsWith("MyCompany.Data") || ns.StartsWith("Vendor.Tools")""", "MyCompany.Data|Vendor.Tools", false, "DataService|SqlService|DataAccessService|ExtraService|LocalDataService|ToolService|ToolExtraService" },
        { """ns => ns == "MyCompany.Data" || ns == "Elsewhere" """, "MyCompany.Data|Elsewhere", false, "DataService|ElsewhereService" },
        { """ns => ns.StartsWith("Vendor.Tools") || ns.Equals("MyCompany.Other")""", "Vendor.Tools|MyCompany.Other", false, "ToolService|ToolExtraService|OtherService" },
        { """ns => (ns.StartsWith("Vendor"))""", "Vendor", false, "ToolService|ToolboxService|ToolExtraService" },
        { """(ns) => ns.StartsWith("MyCompany.Other")""", "MyCompany.Other", false, "OtherService" },
        { """ns => ns.StartsWith("MyCompany")""", "MyCompany", false, "RootService|DataService|SqlService|DataAccessService|OtherService|ExtraService|LocalDataService" },
        { """ns => ns.StartsWith("MyCompany.")""", "MyCompany.", false, "DataService|SqlService|DataAccessService|OtherService|ExtraService|LocalDataService" },
        { """ns => ns.StartsWith("Unrelated.Deep")""", "Unrelated.Deep", false, "DeepService" },
        { """ns => ns.StartsWith("MyCompany.Data.Sql.Internal")""", "MyCompany.Data.Sql.Internal", false, "" },
        { """nm => nm.StartsWith("Elsewhere")""", "Elsewhere", false, "ElsewhereService" },
    };

    [Theory]
    [MemberData(nameof(PrunablePredicates))]
    public void PrunablePredicate_WalksOnlyNamespacesUnderItsPrefixes(string predicate, string prefixes, bool ignoreCase, string expected)
    {
        predicate = predicate.TrimEnd();
        var run = RunRule($".ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)");

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var bounds = prefixes.Split('|');

        foreach (var package in new[] { run.Platform, run.Extras })
        {
            Assert.False(TypeCollectorProbe.IsAssemblyWalked(package), $"{package.Name} was walked as a whole for '{predicate}'.");
            Assert.All(
                TypeCollectorProbe.WalkedNamespaces(package),
                ns => Assert.True(bounds.Any(b => ns.StartsWith(b, comparison)), $"{package.Name}: namespace '{ns}' was walked for '{predicate}'."));
        }

        var platformMatches = expected.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Count(name => name is not ("ExtraService" or "ToolExtraService" or "ElsewhereService" or "LocalDataService"));
        if (platformMatches > 0)
            Assert.NotEmpty(TypeCollectorProbe.WalkedNamespaces(run.Platform));

        Assert.Equal(
            expected.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(ExpectedRegistration).OrderBy(r => r, StringComparer.Ordinal),
            run.Registrations(ConventionDispatch.ByNamespaceFile));
    }

    [Theory]
    [MemberData(nameof(PrunablePredicates))]
    public void PrunablePredicate_RegistersExactlyWhatTheSameUnprunablePredicateRegisters(string predicate, string prefixes, bool ignoreCase, string expected)
    {
        _ = (prefixes, ignoreCase, expected);
        predicate = predicate.TrimEnd();

        var pruned = RunRule($".ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)");
        var unpruned = RunRule($".ByNamespace({Unprunable(predicate)}, Lifetime.Scoped, ConventionScope.AllReferences)");

        Assert.True(TypeCollectorProbe.IsAssemblyWalked(unpruned.Platform));
        Assert.True(TypeCollectorProbe.IsAssemblyWalked(unpruned.Extras));

        Assert.Equal(
            unpruned.Registrations(ConventionDispatch.ByNamespaceFile),
            pruned.Registrations(ConventionDispatch.ByNamespaceFile));
    }

    [Fact]
    public void PrunedAndUnprunedRules_InOneCompilation_RegisterTheSameTypes()
    {
        const string pruned = """ns => ns.StartsWith("MyCompany.Data")""";
        var unpruned = Unprunable(pruned);

        var run = RunRule($"""
            .ByNamespace({pruned}, Lifetime.Scoped, ConventionScope.AllReferences)
                        .ByNamespace({unpruned}, Lifetime.Scoped, ConventionScope.AllReferences)
            """);

        var generated = run.Result.GeneratedSource(ConventionDispatch.ByNamespaceFile);
        var prunedBranch = ConventionDispatch.Branch(generated, "Scoped", "AllReferences", pruned);
        var unprunedBranch = ConventionDispatch.Branch(generated, "Scoped", "AllReferences", unpruned);
        Assert.NotNull(prunedBranch);
        Assert.NotNull(unprunedBranch);
        Assert.Equal(unprunedBranch.Lines, prunedBranch.Lines);
    }

    [Fact]
    public void OrOfStartsWithWithDifferentComparisons_RegistersExactlyWhatTheUnprunablePredicateRegisters()
    {
        const string predicate = """ns => ns.StartsWith("MyCompany.Data") || ns.StartsWith("vendor.tools", StringComparison.OrdinalIgnoreCase)""";

        var run = RunRule($".ByNamespace({predicate}, Lifetime.Scoped, ConventionScope.AllReferences)");
        var unpruned = RunRule($".ByNamespace({Unprunable(predicate)}, Lifetime.Scoped, ConventionScope.AllReferences)");

        Assert.Equal(
            "DataService|SqlService|DataAccessService|ExtraService|LocalDataService|ToolService|ToolExtraService".Split('|')
                .Select(ExpectedRegistration).OrderBy(r => r, StringComparer.Ordinal),
            run.Registrations(ConventionDispatch.ByNamespaceFile));
        Assert.Equal(unpruned.Registrations(ConventionDispatch.ByNamespaceFile), run.Registrations(ConventionDispatch.ByNamespaceFile));
    }

    public static TheoryData<string, string> UnprunablePredicates => new()
    {
        { """.ByNamespace(ns => ns.EndsWith(".Data"), Lifetime.Scoped, ConventionScope.AllReferences)""", "DataService" },
        { """.ByNamespace(ns => ns.Contains("Tool"), Lifetime.Scoped, ConventionScope.AllReferences)""", "ToolService|ToolboxService|ToolExtraService" },
        { """.ByNamespace(ns => !ns.StartsWith("MyCompany") && !ns.StartsWith("Vendor") && !ns.StartsWith("Microsoft") && ns != "Elsewhere", Lifetime.Scoped, ConventionScope.AllReferences)""", "GlobalService|DeepService|LocalOtherService" },
        { """.ByNamespace(ns => ns != "MyCompany.Data" && ns.EndsWith("Inside"), Lifetime.Scoped, ConventionScope.AllReferences)""", "DeepService" },
        { """.ByNamespace(ns => ns.StartsWith("Elsewhere") || ns.EndsWith(".Sql"), Lifetime.Scoped, ConventionScope.AllReferences)""", "ElsewhereService|SqlService" },
        { """.ByNamespace(ns => ns.EndsWith("Deep.Inside") || ns.StartsWith("Elsewhere"), Lifetime.Scoped, ConventionScope.AllReferences)""", "DeepService|ElsewhereService" },
        { """.ByTypeName((ns, name) => ns.StartsWith("MyCompany.Data"), Lifetime.Scoped, ConventionScope.AllReferences)""", "DataService|SqlService|DataAccessService|ExtraService|LocalDataService" },
        { """.ByTypeName((ns, name) => ns == "Elsewhere", Lifetime.Scoped, ConventionScope.AllReferences)""", "ElsewhereService" },
        { """.ByTypeName((ns, name) => name.StartsWith("Tool"), Lifetime.Scoped, ConventionScope.AllReferences)""", "ToolService|ToolboxService|ToolExtraService" },
    };

    [Theory]
    [MemberData(nameof(UnprunablePredicates))]
    public void UnprunablePredicate_WalksEveryPackage_AndRegistersWhatItMatches(string rule, string expected)
    {
        var run = RunRule(rule);

        Assert.True(TypeCollectorProbe.IsAssemblyWalked(run.Platform), $"{PlatformName} was not walked for {rule}.");
        Assert.True(TypeCollectorProbe.IsAssemblyWalked(run.Extras), $"{ExtrasName} was not walked for {rule}.");

        var file = rule.StartsWith(".ByTypeName", StringComparison.Ordinal) ? ConventionDispatch.ByTypeNameFile : ConventionDispatch.ByNamespaceFile;
        Assert.Equal(
            expected.Split('|').Select(ExpectedRegistration).OrderBy(r => r, StringComparer.Ordinal),
            run.Registrations(file));
    }

    [Fact]
    public void MatchAllPredicate_WalksEveryPackage()
    {
        var run = RunRule(".ByNamespace(_ => true, Lifetime.Scoped, ConventionScope.AllReferences)");

        Assert.True(TypeCollectorProbe.IsAssemblyWalked(run.Platform));
        Assert.True(TypeCollectorProbe.IsAssemblyWalked(run.Extras));

        var registrations = run.Registrations(ConventionDispatch.ByNamespaceFile);
        Assert.All(Implementations.Keys, name => Assert.Contains(ExpectedRegistration(name), registrations));
    }

    [Theory]
    [InlineData("MyCompany.Data")]
    [InlineData("Vendor.Tools")]
    [InlineData("Elsewhere")]
    public void ByTypeNameNamespacePredicate_RegistersTheSameAsThePrunedByNamespacePredicate(string prefix)
    {
        var run = RunRule($$"""
            .ByNamespace(ns => ns.StartsWith("{{prefix}}"), Lifetime.Scoped, ConventionScope.AllReferences)
                        .ByTypeName((ns, name) => ns.StartsWith("{{prefix}}"), Lifetime.Scoped, ConventionScope.AllReferences)
            """);

        Assert.NotEmpty(run.Registrations(ConventionDispatch.ByNamespaceFile));
        Assert.Equal(run.Registrations(ConventionDispatch.ByNamespaceFile), run.Registrations(ConventionDispatch.ByTypeNameFile));
    }

    [Theory]
    [InlineData(""".ByNamespace(ns => ns.EndsWith(".Data"), Lifetime.Scoped)""")]
    [InlineData(""".ByNamespace(_ => true, Lifetime.Scoped, ConventionScope.Compilation)""")]
    [InlineData(""".ByNamespace(ns => ns.StartsWith("MyCompany.Data"), Lifetime.Scoped)""")]
    [InlineData(""".ByTypeName((ns, name) => true, Lifetime.Scoped)""")]
    public void CompilationScope_NeverWalksAnyPackage(string rule)
    {
        var run = RunRule(rule);

        Assert.True(TypeCollectorProbe.IsNeverWalked(run.Platform));
        Assert.True(TypeCollectorProbe.IsNeverWalked(run.Extras));

        var generated = (run.Result.GeneratedSource(ConventionDispatch.ByNamespaceFile) ?? "") + (run.Result.GeneratedSource(ConventionDispatch.ByTypeNameFile) ?? "");
        Assert.All(
            Implementations.Keys.Where(n => !n.StartsWith("Local", StringComparison.Ordinal)),
            name => Assert.DoesNotContain($"global::{(Implementations[name].Length == 0 ? "" : Implementations[name] + ".")}{name}>", generated));
    }

    private static (Compilation Compilation, IAssemblySymbol Platform) PlatformCompilation()
    {
        var compilation = PackageCompilationHelper.CreateCompilation("", PackageGraph());
        return (compilation, TypeCollectorProbe.ReferencedAssembly(compilation, PlatformName));
    }

    private static IEnumerable<INamedTypeSymbol> PublicTypes(INamespaceSymbol ns)
    {
        static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type) =>
            type.DeclaredAccessibility != Accessibility.Public
                ? []
                : new[] { type }.Concat(type.GetTypeMembers().SelectMany(WithNested));

        return ns.GetTypeMembers().SelectMany(WithNested).Concat(ns.GetNamespaceMembers().SelectMany(PublicTypes));
    }

    [Theory]
    [InlineData("MyCompany", false)]
    [InlineData("MyCompany.", false)]
    [InlineData("MyCompany.Da", false)]
    [InlineData("MyCompany.Data", false)]
    [InlineData("MyCompany.Data.", false)]
    [InlineData("mycompany.data", false)]
    [InlineData("mycompany.data", true)]
    [InlineData("Vendor.Tool", false)]
    [InlineData("Unrelated.Deep.Inside", false)]
    [InlineData("Unrelated.Deep.Inside.", false)]
    [InlineData("Nope", false)]
    [InlineData("M", false)]
    public void PrunedWalk_ReturnsExactlyThePublicTypesWhoseNamespaceStartsWithThePrefix(string prefix, bool ignoreCase)
    {
        var (_, platform) = PlatformCompilation();
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var pruned = TypeCollector.GetAccessibleTypes(platform, [prefix], comparison)
            .Select(t => t.ToDisplayString())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.False(TypeCollectorProbe.IsAssemblyWalked(platform));
        Assert.All(TypeCollectorProbe.WalkedNamespaces(platform), ns => Assert.StartsWith(prefix, ns, comparison));

        var expected = PublicTypes(platform.GlobalNamespace)
            .Where(t => !t.ContainingNamespace.IsGlobalNamespace && t.ContainingNamespace.ToDisplayString().StartsWith(prefix, comparison))
            .Select(t => t.ToDisplayString())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, pruned);
    }

    [Fact]
    public void PrunedWalk_EmptyPrefix_IsTheWholeAssembly()
    {
        var (_, platform) = PlatformCompilation();

        var all = TypeCollector.GetAccessibleTypes(platform, ["MyCompany.Data", ""], StringComparison.Ordinal).ToList();

        Assert.True(TypeCollectorProbe.IsAssemblyWalked(platform));
        Assert.Equal(
            PublicTypes(platform.GlobalNamespace).Select(t => t.ToDisplayString()).OrderBy(n => n, StringComparer.Ordinal),
            all.Select(t => t.ToDisplayString()).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void PrunedWalk_IsCachedPerNamespace()
    {
        var (_, platform) = PlatformCompilation();

        var first = TypeCollector.GetAccessibleTypes(platform, ["MyCompany.Data"], StringComparison.Ordinal).ToList();
        var dataWalk = TypeCollectorProbe.NamespaceWalk(TypeCollectorProbe.Namespace(platform, "MyCompany.Data"));
        Assert.NotNull(dataWalk);

        var second = TypeCollector.GetAccessibleTypes(platform, ["MyCompany.Data"], StringComparison.Ordinal).ToList();

        Assert.Same(dataWalk, TypeCollectorProbe.NamespaceWalk(TypeCollectorProbe.Namespace(platform, "MyCompany.Data")));
        Assert.Equal(first, second);
    }

    [Fact]
    public void PackageAssemblies_AreThePackagesOnly_NotTheSdkTargetingPacksOrProjectReferences()
    {
        const string probe = "namespace Probe { public interface IProbe { } public class Probe : IProbe { } }";
        var compilation = PackageCompilationHelper.CreateCompilation(
            "",
            ReferencedAssembly.Package("Direct.Package", probe),
            ReferencedAssembly.Package("Transitive.Package", probe, @"C:\Users\dev\.nuget\packages\transitive.package\3.0.0\lib\netstandard2.0\Transitive.Package.dll"),
            ReferencedAssembly.Project("Sibling.Project", probe),
            ReferencedAssembly.Package("Targeting.Pack", probe, "/home/test/.nuget/packages/microsoft.netcore.app.ref/9.0.0/ref/net9.0/Targeting.Pack.dll"),
            ReferencedAssembly.Package("NetStandard.Pack", probe, "/home/test/.nuget/packages/netstandard.library/2.0.3/build/netstandard2.0/ref/NetStandard.Pack.dll"),
            ReferencedAssembly.Package("Sdk.Pack", probe, "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.0/ref/net8.0/Sdk.Pack.dll"));

        Assert.Equal(
            ["Direct.Package", "Microsoft.Extensions.DependencyInjection.Abstractions", "Transitive.Package"],
            TypeCollector.GetPackageReferenceAssemblies(compilation).Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal));

        Assert.Equal(["Sibling.Project"], TypeCollector.GetProjectReferenceAssemblies(compilation).Select(a => a.Name));
    }
}
