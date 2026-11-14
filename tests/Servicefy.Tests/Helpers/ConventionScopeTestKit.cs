namespace Servicefy.Tests.Helpers;

internal static class ConventionScopeTestKit
{
    public const string SharedName = "MyCompany.Shared";
    public const string SdkName = "MyCompany.Sdk";

    public const string IClock = "MyCompany.Shared.Clock.IClock";
    public const string SystemClock = "MyCompany.Shared.Clock.SystemClock";
    public const string IHttpGateway = "MyCompany.Sdk.Http.IHttpGateway";
    public const string HttpGateway = "MyCompany.Sdk.Http.HttpGateway";

    public static ReferencedAssembly SharedPackage(string extraSource = "", string? filePath = null) => ReferencedAssembly.Package(SharedName, $$"""
        using System;

        namespace MyCompany.Shared.Clock
        {
            public interface IClock { string Name { get; } }
            public class SystemClock : IClock { public string Name => "system"; }
        }

        {{extraSource}}
        """, filePath);

    public static ReferencedAssembly SdkPackage(string extraSource = "") => ReferencedAssembly.Package(SdkName, $$"""
        using System;

        namespace MyCompany.Sdk.Http
        {
            public interface IHttpGateway { }
            public class HttpGateway : IHttpGateway
            {
                public HttpGateway(MyCompany.Shared.Clock.IClock clock) { }
            }
        }

        {{extraSource}}
        """);

    public static ReferencedAssembly[] Packages(string sharedExtra = "", string sdkExtra = "") =>
        [SharedPackage(sharedExtra), SdkPackage(sdkExtra)];

    public static string Consumer(string types, string rules) => $$"""
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

    public static string Registration(string lifetime, string service, string implementation) =>
        $"Services.Add{lifetime}<global::{service}, global::{implementation}>();";

    public static void AssertCleanBuild(PackageRunResult result)
    {
        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.CompilationErrors);
    }
}
