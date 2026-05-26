using Microsoft.CodeAnalysis.CSharp;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class GenericAttributeEmissionTests
{
    [Theory]
    [InlineData(LanguageVersion.CSharp11)]
    [InlineData(LanguageVersion.CSharp12)]
    [InlineData(LanguageVersion.CSharp13)]
    public void CSharp11OrLater_EmitsGenericAddScopedAttribute(LanguageVersion version)
    {
        var (output, _) = CompilationHelper.RunGenerator("", version);

        var generic = output.GetTypeByMetadataName("TestAssembly.AddScopedAttribute`1");
        Assert.NotNull(generic);
    }

    [Theory]
    [InlineData(LanguageVersion.CSharp8)]
    [InlineData(LanguageVersion.CSharp9)]
    [InlineData(LanguageVersion.CSharp10)]
    public void CSharpBelow11_DoesNotEmitGenericAddScopedAttribute(LanguageVersion version)
    {
        var (output, _) = CompilationHelper.RunGenerator("", version);

        var generic = output.GetTypeByMetadataName("TestAssembly.AddScopedAttribute`1");
        Assert.Null(generic);
    }

    [Theory]
    [InlineData(LanguageVersion.CSharp8)]
    [InlineData(LanguageVersion.CSharp9)]
    [InlineData(LanguageVersion.CSharp10)]
    public void CSharpBelow11_StillEmitsNonGenericAddScopedAttribute(LanguageVersion version)
    {
        var (output, _) = CompilationHelper.RunGenerator("", version);

        var nonGeneric = output.GetTypeByMetadataName("TestAssembly.AddScopedAttribute");
        Assert.NotNull(nonGeneric);
    }

    [Theory]
    [InlineData(LanguageVersion.CSharp11)]
    [InlineData(LanguageVersion.CSharp8)]
    public void AllVersions_EmitsNonGenericAddSingletonAttribute(LanguageVersion version)
    {
        var (output, _) = CompilationHelper.RunGenerator("", version);

        var nonGeneric = output.GetTypeByMetadataName("TestAssembly.AddSingletonAttribute");
        Assert.NotNull(nonGeneric);
    }

    [Fact]
    public void CSharp11_GenericSyntax_RegistersCorrectly()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped<IMyService>]
                public class MyService : IMyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source, LanguageVersion.CSharp11);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("IMyService", generatedSource);
        Assert.Contains("MyService", generatedSource);
    }

    [Fact]
    public void CSharp8_TypeofSyntax_RegistersCorrectly()
    {
        var source = """
            using TestAssembly;
            namespace TestAssembly
            {
                public interface IMyService { }

                [AddScoped(typeof(IMyService))]
                public class MyService : IMyService { }
            }
            """;

        var (output, diagnostics) = CompilationHelper.RunGenerator(source, LanguageVersion.CSharp8);

        Assert.Empty(diagnostics);
        var generatedSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("ServicefyExtensions"));

        Assert.NotNull(generatedSource);
        Assert.Contains("IMyService", generatedSource);
        Assert.Contains("MyService", generatedSource);
    }
}
