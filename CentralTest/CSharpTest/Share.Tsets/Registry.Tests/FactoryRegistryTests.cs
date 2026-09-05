using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace Registry.Tests;

[TestClass]
public class FactoryRegistryTests : TestBase
{
    [TestMethod]
    public void RunAll_BasicOperations()
    {
        var result = new TestGroupBuilder("FactoryRegistry_Basic")
            .Add("Register_And_Resolve_ShouldCreateInstance", () =>
            {
                var registry = new FactoryRegistryBase<string, string>();
                registry.Register("hello", sp => "Hello World");
                var instance = registry.Resolve("hello");
                return instance == "Hello World";
            })
            .Add("Resolve_NonExistent_ShouldReturnDefault", () =>
            {
                var registry = new FactoryRegistryBase<string, string>();
                return registry.Resolve("nonexistent") == null;
            })
            .Add("Resolve_MultipleTimes_ShouldCreateNewEachTime", () =>
            {
                var registry = new FactoryRegistryBase<string, int>();
                int counter = 0;
                registry.Register("counter", sp => ++counter);
                var v1 = registry.Resolve("counter");
                var v2 = registry.Resolve("counter");
                return v1 == 1 && v2 == 2;
            })
            .Add("Contains_ShouldWork", () =>
            {
                var registry = new FactoryRegistryBase<string, string>();
                registry.Register("key", sp => "value");
                return registry.Contains("key") && !registry.Contains("nonexistent");
            })
            .Add("Unregister_ShouldRemoveFactory", () =>
            {
                var registry = new FactoryRegistryBase<string, string>();
                registry.Register("key", sp => "value");
                registry.Unregister("key");
                return !registry.Contains("key") && registry.Resolve("key") == null;
            })
            .Add("Clear_ShouldRemoveAll", () =>
            {
                var registry = new FactoryRegistryBase<string, string>();
                registry.Register("a", sp => "1");
                registry.Register("b", sp => "2");
                registry.Clear();
                return registry.Count == 0;
            })
            .Add("GetKeys_ShouldReturnAllKeys", () =>
            {
                var registry = new FactoryRegistryBase<string, string>();
                registry.Register("a", sp => "1");
                registry.Register("b", sp => "2");
                var keys = registry.GetKeys().ToList();
                return keys.Count == 2 && keys.Contains("a") && keys.Contains("b");
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_WithServiceProvider()
    {
        var result = new TestGroupBuilder("FactoryRegistry_ServiceProvider")
            .Add("Resolve_WithServiceProvider_ShouldPassToFactory", () =>
            {
                var registry = new FactoryRegistryBase<string, string>();
                var services = new Dictionary<string, object> { ["String"] = "SqlConnection" };
                var serviceProvider = new MockServiceProvider(services);

                registry.Register("service", sp =>
                {
                    var db = sp?.GetService(typeof(string)) as string;
                    return $"Resolved: {db}";
                });

                var instance = registry.Resolve("service", serviceProvider);
                return instance == "Resolved: SqlConnection";
            })
            .Add("Resolve_WithoutServiceProvider_ShouldPassNull", () =>
            {
                var registry = new FactoryRegistryBase<string, string>();
                bool wasNull = false;
                registry.Register("test", sp => { wasNull = sp == null; return "ok"; });
                registry.Resolve("test");
                return wasNull;
            })
            .Run();

        result.AssertAllPassed();
    }

    private class MockServiceProvider : IServiceProvider
    {
        private readonly Dictionary<string, object> _services;

        public MockServiceProvider(Dictionary<string, object> services)
        {
            _services = services;
        }

        public object? GetService(Type serviceType)
        {
            return _services.TryGetValue(serviceType.Name, out var value) ? value : null;
        }
    }
}
