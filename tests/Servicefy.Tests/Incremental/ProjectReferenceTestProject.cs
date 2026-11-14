using Microsoft.CodeAnalysis;
using Servicefy.Tests.Helpers;

namespace Servicefy.Tests.Incremental;

internal static class ProjectReferenceTestProject
{
    public const string FeaturesAssemblyName = "Features";

    public const string StartupPath = "Startup.cs";
    public const string LocalPath = "Local.cs";
    public const string UtilitiesPath = "Utilities.cs";

    public const string BillingService = "global::Features.Billing.IBillingService";
    public const string RefundService = "global::Features.Billing.IRefundService";
    public const string CustomerRepository = "global::Features.Data.IRepository<global::Features.Data.Customer>";

    public const string FeaturesV1 = """
        using System;

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
        public sealed class DecoratorForAttribute<T> : Attribute { }

        namespace Features.Billing
        {
            public interface IBillingService { }
            public class BillingService : IBillingService { }

            [DecoratorFor<IBillingService>]
            public class AuditBillingDecorator : IBillingService
            {
                public AuditBillingDecorator(IBillingService inner) { }
            }

            public class LoggingBillingDecorator : IBillingService
            {
                public LoggingBillingDecorator(IBillingService inner) { }
            }

            internal class InternalBillingService : IBillingService { }
        }

        namespace Features.Data
        {
            public interface IRepository<T> { }
            public class Customer { }
            public class CustomerRepository : IRepository<Customer> { }

            public class LoggingRepository<T> : IRepository<T>
            {
                public LoggingRepository(IRepository<T> inner) { }
            }
        }
        """;

    public static readonly string FeaturesV2 = ReplaceOnce(
        FeaturesV1,
        "    internal class InternalBillingService : IBillingService { }",
        """
                    internal class InternalBillingService : IBillingService { }

                    [DecoratorFor<IBillingService>]
                    public class RetryBillingDecorator : IBillingService
                    {
                        public RetryBillingDecorator(IBillingService inner) { }
                    }

                    public interface IRefundService { }
                    public class RefundService : IRefundService { }
        """);

    private static string ReplaceOnce(string text, string oldText, string newText)
    {
        var index = text.IndexOf(oldText, StringComparison.Ordinal);
        if (index < 0 || text.IndexOf(oldText, index + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"'{oldText}' must occur exactly once.");
        return text.Remove(index, oldText.Length).Insert(index, newText);
    }

    public const string LoggingDecorateCall = "services.Decorate<IBillingService, LoggingBillingDecorator>();";

    public const string Startup = """
        using Features.Billing;
        using Features.Data;

        namespace TestAssembly
        {
            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.Decorate<IBillingService, LoggingBillingDecorator>();
                    services.AddServicefyConventions().ByNamespace(ns => ns == "Features.Billing", Lifetime.Scoped);
                    services.AddServicefyConventions()
                        .ByBaseType(typeof(IRepository<>), Lifetime.Transient)
                        .Decorate(typeof(IRepository<>), typeof(LoggingRepository<>));
                }
            }
        }
        """;

    public const string Local = """
        using Features.Billing;

        namespace TestAssembly.Local
        {
            public class TimingBillingDecorator : IBillingService
            {
                public TimingBillingDecorator(IBillingService inner) { }
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
        (StartupPath, Startup),
        (LocalPath, Local),
        (UtilitiesPath, Utilities),
    ];

    public static PortableExecutableReference Features(string source) =>
        CompilationHelper.CreateProjectReference(source, FeaturesAssemblyName);
}
