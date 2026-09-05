using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace Registry.Tests;

[TestClass]
public class MapRegistryTests : TestBase
{
    [TestMethod]
    public void RunAll_BasicOperations()
    {
        var result = new TestGroupBuilder("MapRegistry_Basic")
            .Add("Register_And_TryGet_ShouldWork", () =>
            {
                var registry = new MapRegistryBase<string, int>();
                registry.Register("one", 1);
                return registry.TryGet("one", out int val) && val == 1;
            })
            .Add("TryGet_NonExistent_ShouldReturnFalse", () =>
            {
                var registry = new MapRegistryBase<string, int>();
                return !registry.TryGet("nonexistent", out int _);
            })
            .Add("Register_Overwrite_ShouldUpdateValue", () =>
            {
                var registry = new MapRegistryBase<string, int>(allowOverwrite: true);
                registry.Register("key", 1);
                registry.Register("key", 2);
                return registry.TryGet("key", out int val) && val == 2 && registry.Count == 1;
            })
            .Add("Register_OverwriteDisabled_ShouldThrow", () =>
            {
                var registry = new MapRegistryBase<string, int>(allowOverwrite: false);
                registry.Register("key", 1);
                try { registry.Register("key", 2); return false; }
                catch (InvalidOperationException) { return true; }
            })
            .Add("Unregister_ShouldRemoveKey", () =>
            {
                var registry = new MapRegistryBase<string, int>();
                registry.Register("key", 42);
                registry.Unregister("key");
                return !registry.TryGet("key", out int _) && registry.Count == 0;
            })
            .Add("Clear_ShouldRemoveAll", () =>
            {
                var registry = new MapRegistryBase<string, int>();
                registry.Register("a", 1); registry.Register("b", 2);
                registry.Clear();
                return registry.Count == 0;
            })
            .Add("Contains_ShouldWork", () =>
            {
                var registry = new MapRegistryBase<string, int>();
                registry.Register("key", 1);
                return registry.Contains("key") && !registry.Contains("nonexistent");
            })
            .Add("GetKeys_ShouldReturnAllKeys", () =>
            {
                var registry = new MapRegistryBase<string, int>();
                registry.Register("a", 1); registry.Register("b", 2);
                var keys = registry.GetKeys().ToList();
                return keys.Count == 2 && keys.Contains("a") && keys.Contains("b");
            })
            .Add("Register_NullKey_ShouldThrow", () =>
            {
                var registry = new MapRegistryBase<string, int>();
                try { registry.Register(null!, 1); return false; }
                catch (ArgumentNullException) { return true; }
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_CustomComparer()
    {
        var result = new TestGroupBuilder("MapRegistry_CustomComparer")
            .Add("CaseInsensitive_ShouldMatch", () =>
            {
                var registry = new MapRegistryBase<string, string>(StringComparer.OrdinalIgnoreCase);
                registry.Register("HELLO", "world");
                return registry.TryGet("hello", out string? val) && val == "world";
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_ValueTypes()
    {
        var result = new TestGroupBuilder("MapRegistry_ValueTypes")
            .Add("IntKey_ShouldWork", () =>
            {
                var registry = new MapRegistryBase<int, string>();
                registry.Register(1, "one");
                registry.Register(2, "two");
                return registry.TryGet(1, out string? v1) && v1 == "one"
                    && registry.TryGet(2, out string? v2) && v2 == "two"
                    && registry.Count == 2;
            })
            .Add("ObjectValue_ShouldWork", () =>
            {
                var registry = new MapRegistryBase<string, object>();
                var obj = new { Name = "test" };
                registry.Register("item", obj);
                return registry.TryGet("item", out object? val) && val == obj;
            })
            .Run();

        result.AssertAllPassed();
    }
}
