using TestInfrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.Registry;

namespace Registry.Tests;

[TestClass]
public class ConcreteRegistryTests : TestBase
{
    [TestMethod]
    public void RunAll_FunctionRegistry()
    {
        var result = new TestGroupBuilder("FunctionRegistry")
            .Add("Register_And_TryGetDescriptor_ShouldWork", () =>
            {
                var registry = new FunctionRegistry();
                registry.Register("hello", () => "world", "Says hello");
                return registry.TryGetDescriptor("hello", out IFunctionDescriptor? desc)
                    && desc != null
                    && desc.Description == "Says hello";
            })
            .Add("TryGetDescriptor_NonExistent_ShouldReturnFalse", () =>
            {
                var registry = new FunctionRegistry();
                return !registry.TryGetDescriptor("nonexistent", out IFunctionDescriptor? _);
            })
            .Add("GetRegisteredNames_ShouldReturnAllNames", () =>
            {
                var registry = new FunctionRegistry();
                registry.Register("a", () => { });
                registry.Register("b", () => { });
                var names = registry.GetRegisteredNames().ToList();
                return names.Count == 2 && names.Contains("a") && names.Contains("b");
            })
            .Add("Register_EmptyName_ShouldThrow", () =>
            {
                var registry = new FunctionRegistry();
                try { registry.Register("", () => { }); return false; }
                catch (ArgumentException) { return true; }
            })
            .Add("Register_NullDelegate_ShouldThrow", () =>
            {
                var registry = new FunctionRegistry();
                try { registry.Register("test", null!); return false; }
                catch (ArgumentNullException) { return true; }
            })
            .Add("CaseInsensitive_ByName", () =>
            {
                var registry = new FunctionRegistry();
                registry.Register("Hello", () => "world");
                return registry.Contains("hello") && registry.Contains("HELLO");
            })
            .Add("Contains_ShouldWork", () =>
            {
                var registry = new FunctionRegistry();
                registry.Register("myFunc", () => { });
                return registry.Contains("myFunc") && !registry.Contains("nonexistent");
            })
            .Add("Unregister_ShouldRemove", () =>
            {
                var registry = new FunctionRegistry();
                registry.Register("temp", () => { });
                registry.Unregister("temp");
                return !registry.Contains("temp");
            })
            .Run();

        result.AssertAllPassed();
    }

    [TestMethod]
    public void RunAll_NormalizationRegistry()
    {
        var result = new TestGroupBuilder("NormalizationRegistry")
            .Add("DefaultMappings_ShouldBeLoaded", () =>
            {
                var registry = new NormalizationRegistry();
                return registry.TryGet("click", out string? full)
                    && full == "do click";
            })
            .Add("TryGet_Unknown_ShouldReturnFalse", () =>
            {
                var registry = new NormalizationRegistry();
                return !registry.TryGet("unknown", out string? _);
            })
            .Add("CustomMapping_ShouldWork", () =>
            {
                var registry = new NormalizationRegistry();
                registry.Register("custom", "do custom");
                return registry.TryGet("custom", out string? full)
                    && full == "do custom";
            })
            .Add("WithoutDefaults_ShouldBeEmpty", () =>
            {
                var registry = new NormalizationRegistry(includeDefaults: false);
                return registry.Count == 0;
            })
            .Add("GetKeys_ShouldReturnAll", () =>
            {
                var registry = new NormalizationRegistry(includeDefaults: true);
                var keys = registry.GetKeys().ToList();
                return keys.Count > 0;
            })
            .Run();

        result.AssertAllPassed();
    }
}
