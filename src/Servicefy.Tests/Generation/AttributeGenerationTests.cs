using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class AttributeGenerationTests
{
    [Fact]
    public void Generator_EmitsAddSingletonAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");

        var type = output.GetTypeByMetadataName("TestAssembly.AddSingletonAttribute");
        Assert.NotNull(type);
    }

    [Fact]
    public void Generator_EmitsAddScopedAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");

        var type = output.GetTypeByMetadataName("TestAssembly.AddScopedAttribute");
        Assert.NotNull(type);
    }

    [Fact]
    public void Generator_EmitsAddTransientAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");

        var type = output.GetTypeByMetadataName("TestAssembly.AddTransientAttribute");
        Assert.NotNull(type);
    }

    [Fact]
    public void Generator_EmitsAddAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");

        var type = output.GetTypeByMetadataName("TestAssembly.AddAttribute");
        Assert.NotNull(type);
    }

    [Fact]
    public void Generator_EmitsConfigureAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");

        var type = output.GetTypeByMetadataName("TestAssembly.ConfigureAttribute");
        Assert.NotNull(type);
    }

    [Fact]
    public void Generator_EmitsLifetimeEnum()
    {
        var (output, _) = CompilationHelper.RunGenerator("");

        var type = output.GetTypeByMetadataName("TestAssembly.Lifetime");
        Assert.NotNull(type);
    }
}
