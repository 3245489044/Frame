using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace Registry.Tests;

[TestClass]
public class MetaMapRegistryTests : TestBase
{
    [TestMethod]
    public void RunAll_BasicOperations()
    {
        var result = new TestGroupBuilder("MetaMapRegistry_Basic")
            .Add("Register_And_TryGet_ShouldWork", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                registry.Register("key", 42, "metadata");
                return registry.TryGet("key", out int val, out string? meta)
                    && val == 42 && meta == "metadata";
            })
            .Add("TryGet_NonExistent_ShouldReturnFalse", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                return !registry.TryGet("nonexistent", out int _, out string? _);
            })
            .Add("TryGetMetadata_ShouldReturnMetadata", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                registry.Register("key", 42, "important");
                return registry.TryGetMetadata("key", out string? meta) && meta == "important";
            })
            .Add("TryGetMetadata_NonExistent_ShouldReturnFalse", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                return !registry.TryGetMetadata("nonexistent", out string? _);
            })
            .Add("Unregister_ShouldRemoveAll", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                registry.Register("key", 42, "meta");
                registry.Unregister("key");
                return !registry.TryGet("key", out int _, out string? _);
            })
            .Add("Clear_ShouldRemoveAll", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                registry.Register("a", 1, "m1");
                registry.Register("b", 2, "m2");
                registry.Clear();
                return registry.Count == 0;
            })
            .Add("Contains_ShouldWork", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                registry.Register("key", 42, "meta");
                return registry.Contains("key") && !registry.Contains("nonexistent");
            })
            .Add("GetKeys_ShouldReturnAllKeys", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                registry.Register("a", 1, "m1");
                registry.Register("b", 2, "m2");
                var keys = registry.GetKeys().ToList();
                return keys.Count == 2 && keys.Contains("a") && keys.Contains("b");
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_UpdateMetadata()
    {
        var result = new TestGroupBuilder("MetaMapRegistry_UpdateMetadata")
            .Add("Register_Overwrite_ShouldUpdateValueAndMetadata", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                registry.Register("key", 1, "old");
                registry.Register("key", 2, "new");
                return registry.TryGet("key", out int val, out string? meta)
                    && val == 2 && meta == "new";
            })
            .Add("Register_WithoutMetadata_ShouldClearMetadata", () =>
            {
                var registry = new MetaMapRegistryBase<string, int, string>();
                registry.Register("key", 1, "old");
                registry.Register("key", 2);
                return registry.TryGet("key", out int val, out string? meta)
                    && val == 2 && meta == null;
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_ComplexMetadata()
    {
        var result = new TestGroupBuilder("MetaMapRegistry_ComplexMetadata")
            .Add("ObjectMetadata_ShouldWork", () =>
            {
                var registry = new MetaMapRegistryBase<string, string, TestMeta>();
                var meta = new TestMeta { Priority = 1, Tags = new[] { "a", "b" } };
                registry.Register("key", "value", meta);
                return registry.TryGetMetadata("key", out TestMeta? retrieved)
                    && retrieved!.Priority == 1
                    && retrieved.Tags.Length == 2;
            })
            .Run();

        result.AssertAllPassed();
    }

    private class TestMeta
    {
        public int Priority { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
    }
}
