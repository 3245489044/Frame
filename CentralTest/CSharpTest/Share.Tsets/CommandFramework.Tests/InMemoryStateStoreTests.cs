using TestInfrastructure;
using CommandFramework.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommandFramework.Tests
{
    [TestClass]
    public class InMemoryStateStoreTests : TestBase
    {
        [TestMethod]
        public void RunAll_BasicOperations()
        {
            var result = new TestGroupBuilder("BasicOperations")
                .Add("Set_And_Get_ShouldReturnValue", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("key1", "value1");
                    return store.Get("key1") == "value1";
                })
                .Add("Set_Overwrite_ShouldUpdateValue", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("key1", "original");
                    store.Set("key1", "updated");
                    return store.Get("key1") == "updated";
                })
                .Add("Set_NullValue_ShouldStoreNull", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("nullKey", null);
                    return store.Get("nullKey") == null;
                })
                .Add("Set_EmptyKey_ShouldThrow", () =>
                {
                    var store = new InMemoryStateStore();
                    try
                    {
                        store.Set("", "value");
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                })
                .Add("Get_NonExistentKey_ShouldReturnNull", () =>
                {
                    var store = new InMemoryStateStore();
                    return store.Get("nonExistent") == null;
                })
                .Add("Get_EmptyKey_ShouldReturnNull", () =>
                {
                    var store = new InMemoryStateStore();
                    return store.Get("") == null;
                })
                .Add("Has_ExistingKey_ShouldReturnTrue", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("exists", "yes");
                    return store.Has("exists");
                })
                .Add("Has_NonExistentKey_ShouldReturnFalse", () =>
                {
                    var store = new InMemoryStateStore();
                    return !store.Has("nonExistent");
                })
                .Add("Has_EmptyKey_ShouldReturnFalse", () =>
                {
                    var store = new InMemoryStateStore();
                    return !store.Has("");
                })
                .Add("Remove_ExistingKey_ShouldRemove", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("toRemove", "value");
                    store.Remove("toRemove");
                    return !store.Has("toRemove");
                })
                .Add("Remove_NonExistentKey_ShouldNotThrow", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Remove("nonExistent");
                    return true;
                })
                .Add("Clear_ShouldRemoveAll", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("a", "1");
                    store.Set("b", "2");
                    store.Clear();
                    return store.Count == 0 && !store.Has("a");
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_AtomicOperations()
        {
            var result = new TestGroupBuilder("AtomicOperations")
                .Add("GetOrAdd_NewKey_ShouldAddAndReturn", () =>
                {
                    var store = new InMemoryStateStore();
                    var value = store.GetOrAdd("newKey", "default");
                    return value == "default" && store.Has("newKey");
                })
                .Add("GetOrAdd_ExistingKey_ShouldReturnExisting", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("existing", "original");
                    var value = store.GetOrAdd("existing", "default");
                    return value == "original";
                })
                .Add("GetOrAdd_EmptyKey_ShouldThrow", () =>
                {
                    var store = new InMemoryStateStore();
                    try
                    {
                        store.GetOrAdd("", "value");
                        return false;
                    }
                    catch (ArgumentException)
                    {
                        return true;
                    }
                })
                .Run();

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_BatchOperations()
        {
            var result = new TestGroupBuilder("BatchOperations")
                .Add("GetAllKeys_ShouldReturnAllKeys", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("k1", "v1");
                    store.Set("k2", "v2");
                    store.Set("k3", "v3");
                    var keys = store.GetAllKeys();
                    return keys.Count == 3
                        && keys.Contains("k1")
                        && keys.Contains("k2")
                        && keys.Contains("k3");
                })
                .Add("GetAllKeys_Empty_ShouldReturnEmpty", () =>
                {
                    var store = new InMemoryStateStore();
                    return store.GetAllKeys().Count == 0;
                })
                .Add("RemoveByPrefix_ShouldRemoveMatching", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("user:1", "Alice");
                    store.Set("user:2", "Bob");
                    store.Set("config:theme", "dark");
                    store.RemoveByPrefix("user:");
                    return !store.Has("user:1")
                        && !store.Has("user:2")
                        && store.Has("config:theme");
                })
                .Add("RemoveByPrefix_EmptyPrefix_ShouldNotRemove", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("key", "value");
                    store.RemoveByPrefix("");
                    return store.Has("key");
                })
                .Add("RemoveByPrefix_NoMatch_ShouldNotRemove", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("only", "value");
                    store.RemoveByPrefix("nonexistent");
                    return store.Count == 1;
                })
                .Add("Count_ShouldReturnCorrectNumber", () =>
                {
                    var store = new InMemoryStateStore();
                    store.Set("a", "1");
                    store.Set("b", "2");
                    return store.Count == 2;
                })
                .Run();

            result.AssertAllPassed();
        }
    }
}
