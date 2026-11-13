namespace Servicefy.Tests.Incremental;

/// <summary>
/// A small multi-file project used by the incremental-step tests: one file with attribute-based
/// (ByConfiguration) registrations and convention targets, one file with one call of each convention
/// (<c>ByNamespace</c>, <c>ByBaseType</c>, <c>ByNamespaceOf</c>, <c>ByTypeName</c>), and one file with
/// plain code unrelated to DI.
/// </summary>
internal static class IncrementalTestProject
{
    public const string ServicesPath = "Services.cs";
    public const string StartupPath = "Startup.cs";
    public const string UtilitiesPath = "Utilities.cs";

    public const string Services = """
        namespace TestAssembly.Services
        {
            public interface IGreeter { }

            [AddScoped]
            public class Greeter : IGreeter { }
        }

        namespace TestAssembly.Repositories
        {
            public class Marker { }

            public interface IOrderRepository { }
            public class OrderRepository : IOrderRepository { }
        }

        namespace TestAssembly.Handlers
        {
            public interface IHandler { }
            public class CreateOrderHandler : IHandler { }
        }
        """;

    public const string Startup = """
        namespace TestAssembly
        {
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddServicefyConventions().ByNamespace(ns => ns.StartsWith("TestAssembly.Repositories"), Lifetime.Scoped);
                    services.AddServicefyConventions().ByBaseType<TestAssembly.Handlers.IHandler>(Lifetime.Transient);
                    services.AddServicefyConventions().ByNamespaceOf<TestAssembly.Repositories.Marker>(ns => ns.EndsWith("Repositories"), Lifetime.Singleton);
                    services.AddServicefyConventions().ByTypeName((ns, name) => name.EndsWith("Repository"), Lifetime.Scoped);
                }
            }
        }
        """;

    public const string Utilities = """
        namespace TestAssembly.Utilities
        {
            public static class MathUtil
            {
                public static int Add(int a, int b)
                {
                    return a + b;
                }
            }
        }
        """;

    public static IEnumerable<(string Path, string Source)> Sources =>
    [
        (ServicesPath, Services),
        (StartupPath, Startup),
        (UtilitiesPath, Utilities),
    ];
}
