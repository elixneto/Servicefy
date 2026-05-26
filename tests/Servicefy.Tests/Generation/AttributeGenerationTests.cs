using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Generation;

public class AttributeGenerationTests
{
    [Fact]
    public void Generator_EmitsAddSingletonAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddSingletonAttribute"));
    }

    [Fact]
    public void Generator_EmitsAddScopedAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddScopedAttribute"));
    }

    [Fact]
    public void Generator_EmitsAddTransientAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddTransientAttribute"));
    }

    [Fact]
    public void Generator_EmitsAddAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddAttribute"));
    }

    [Fact]
    public void Generator_EmitsConfigureAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.ConfigureAttribute"));
    }

    [Fact]
    public void Generator_EmitsLifetimeEnum()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.Lifetime"));
    }

    [Fact]
    public void Generator_EmitsAddKeyedScopedAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddKeyedScopedAttribute"));
    }

    [Fact]
    public void Generator_EmitsAddKeyedSingletonAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddKeyedSingletonAttribute"));
    }

    [Fact]
    public void Generator_EmitsAddKeyedTransientAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddKeyedTransientAttribute"));
    }

    [Fact]
    public void Generator_EmitsGenericAddScopedAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddScopedAttribute`1"));
    }

    [Fact]
    public void Generator_EmitsGenericAddSingletonAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddSingletonAttribute`1"));
    }

    [Fact]
    public void Generator_EmitsGenericAddTransientAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddTransientAttribute`1"));
    }

    [Fact]
    public void Generator_EmitsGenericAddKeyedScopedAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddKeyedScopedAttribute`1"));
    }

    [Fact]
    public void Generator_EmitsGenericAddKeyedSingletonAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddKeyedSingletonAttribute`1"));
    }

    [Fact]
    public void Generator_EmitsGenericAddKeyedTransientAttribute()
    {
        var (output, _) = CompilationHelper.RunGenerator("");
        Assert.NotNull(output.GetTypeByMetadataName("TestAssembly.AddKeyedTransientAttribute`1"));
    }
}
