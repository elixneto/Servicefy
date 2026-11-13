namespace Servicefy.Tests.Incremental;

internal static class DecoratorTestProject
{
    public const string ContractsPath = "Contracts.cs";
    public const string ServicesPath = "Services.cs";
    public const string DecoratorsPath = "Decorators.cs";
    public const string StartupPath = "Startup.cs";
    public const string UtilitiesPath = "Utilities.cs";

    public const string OrderService = "global::TestAssembly.Orders.IOrderService";
    public const string PaymentService = "global::TestAssembly.Payments.IPaymentService";
    public const string OrderRepository = "global::TestAssembly.Data.IRepository<global::TestAssembly.Data.Order>";
    public const string InvoiceRepository = "global::TestAssembly.Data.IRepository<global::TestAssembly.Data.Invoice>";

    public const string Contracts = """
        namespace TestAssembly.Orders
        {
            public interface IOrderService { }
        }

        namespace TestAssembly.Payments
        {
            public interface IPaymentService { }
        }

        namespace TestAssembly.Data
        {
            public interface IRepository<T> { }
            public class Order { }
            public class Invoice { }
        }
        """;

    public const string Services = """
        namespace TestAssembly.Orders
        {
            [AddScoped]
            public class OrderService : IOrderService { }
        }

        namespace TestAssembly.Payments
        {
            public class PaymentService : IPaymentService { }
        }

        namespace TestAssembly.Data
        {
            public class OrderRepository : IRepository<Order> { }
            public class InvoiceRepository : IRepository<Invoice> { }
        }
        """;

    public const string Decorators = """
        namespace TestAssembly.Orders
        {
            [DecoratorFor<IOrderService>]
            public class AuditOrderDecorator : IOrderService
            {
                public AuditOrderDecorator(IOrderService inner) { }
            }

            public class LoggingOrderDecorator : IOrderService
            {
                public LoggingOrderDecorator(IOrderService inner) { }
            }

            public class CachingOrderDecorator : IOrderService
            {
                public CachingOrderDecorator(IOrderService inner) { }
            }

            public class RetryOrderDecorator : IOrderService
            {
                public RetryOrderDecorator(IOrderService inner) { }
            }
        }

        namespace TestAssembly.Payments
        {
            [Decorator]
            public class MetricsPaymentDecorator : IPaymentService
            {
                public MetricsPaymentDecorator(IPaymentService inner) { }
            }

            public class LoggingPaymentDecorator : IPaymentService
            {
                public LoggingPaymentDecorator(IPaymentService inner) { }
            }
        }

        namespace TestAssembly.Data
        {
            public class LoggingRepository<T> : IRepository<T>
            {
                public LoggingRepository(IRepository<T> inner) { }
            }

            public class CachingRepository<T> : IRepository<T>
            {
                public CachingRepository(IRepository<T> inner) { }
            }
        }
        """;

    public const string OrderChain =
        "services.Decorate<IOrderService, LoggingOrderDecorator>()\n" +
        "                .Decorate<IOrderService, CachingOrderDecorator>();";

    public const string PaymentDecorateCall = "services.Decorate<IPaymentService, LoggingPaymentDecorator>();";

    public const string ByNamespaceCall =
        "services.AddServicefyConventions().ByNamespace(ns => ns == \"TestAssembly.Payments\", Lifetime.Scoped);";

    public const string OpenGenericDecorate = ".Decorate(typeof(IRepository<>), typeof(LoggingRepository<>));";

    public const string Startup = """
        using TestAssembly.Data;
        using TestAssembly.Orders;
        using TestAssembly.Payments;

        namespace TestAssembly
        {
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.Decorate<IOrderService, LoggingOrderDecorator>()
                        .Decorate<IOrderService, CachingOrderDecorator>();
                    services.Decorate<IPaymentService, LoggingPaymentDecorator>();
                    services.AddServicefyConventions().ByNamespace(ns => ns == "TestAssembly.Payments", Lifetime.Scoped);
                    services.AddServicefyConventions()
                        .ByBaseType(typeof(IRepository<>), Lifetime.Scoped)
                        .Decorate(typeof(IRepository<>), typeof(LoggingRepository<>));
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
        (ContractsPath, Contracts),
        (ServicesPath, Services),
        (DecoratorsPath, Decorators),
        (StartupPath, Startup),
        (UtilitiesPath, Utilities),
    ];
}
