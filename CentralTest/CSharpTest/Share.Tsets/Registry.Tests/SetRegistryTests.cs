using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace Registry.Tests;

[TestClass]
public class SetRegistryTests : TestBase
{
    [TestMethod]
    public void RunAll_BasicOperations()
    {
        var result = new TestGroupBuilder("SetRegistry_Basic")
            .Add("Register_ShouldAddKey", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.Register("key1");
                return registry.Contains("key1");
            })
            .Add("Register_Duplicate_ShouldNotThrow", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.Register("key1");
                registry.Register("key1");
                return registry.Count == 1;
            })
            .Add("Unregister_ShouldRemoveKey", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.Register("key1");
                registry.Unregister("key1");
                return !registry.Contains("key1") && registry.Count == 0;
            })
            .Add("Unregister_NonExistent_ShouldNotThrow", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.Unregister("nonexistent");
                return true;
            })
            .Add("Clear_ShouldRemoveAll", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.Register("a"); registry.Register("b"); registry.Register("c");
                registry.Clear();
                return registry.Count == 0 && !registry.Contains("a");
            })
            .Add("Count_ShouldReflectRegisteredKeys", () =>
            {
                var registry = new SetRegistryBase<string>();
                Assert.AreEqual(0, registry.Count);
                registry.Register("x");
                Assert.AreEqual(1, registry.Count);
                registry.Register("y");
                Assert.AreEqual(2, registry.Count);
                registry.Unregister("x");
                Assert.AreEqual(1, registry.Count);
                return true;
            })
            .Add("GetKeys_ShouldReturnAllKeys", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.Register("a"); registry.Register("b");
                var keys = registry.GetKeys().ToList();
                return keys.Count == 2 && keys.Contains("a") && keys.Contains("b");
            })
            .Add("Register_NullKey_ShouldThrow", () =>
            {
                var registry = new SetRegistryBase<string>();
                try { registry.Register(null!); return false; }
                catch (ArgumentNullException) { return true; }
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_CustomComparer()
    {
        var result = new TestGroupBuilder("SetRegistry_CustomComparer")
            .Add("CaseInsensitiveComparer_ShouldWork", () =>
            {
                var registry = new SetRegistryBase<string>(StringComparer.OrdinalIgnoreCase);
                registry.Register("KEY");
                return registry.Contains("key") && registry.Contains("KEY") && registry.Count == 1;
            })
            .Add("CaseSensitiveComparer_Default", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.Register("KEY");
                return !registry.Contains("key") && registry.Contains("KEY");
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_Extensions()
    {
        var result = new TestGroupBuilder("SetRegistry_Extensions")
            .Add("RegisterRange_ShouldAddAll", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.RegisterRange(new[] { "a", "b", "c" });
                return registry.Count == 3
                    && registry.Contains("a")
                    && registry.Contains("b")
                    && registry.Contains("c");
            })
            .Add("UnregisterRange_ShouldRemoveAll", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.RegisterRange(new[] { "a", "b", "c" });
                registry.UnregisterRange(new[] { "a", "c" });
                return registry.Count == 1 && registry.Contains("b");
            })
            .Add("GetKeysArray_ShouldReturnArray", () =>
            {
                var registry = new SetRegistryBase<string>();
                registry.RegisterRange(new[] { "x", "y" });
                var arr = registry.GetKeysArray();
                return arr.Length == 2;
            })
            .Run();

        result.AssertAllPassed();
    }
}
