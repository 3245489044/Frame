using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Operation.StateBlock.Core;

namespace StateHoldDriven.Tests
{
    /// <summary>
    /// StateStore（IStorageCell 后端）单测——P1 存储收敛验证：
    /// Set/Get/Remove/RemoveByPrefix/StateChanged 触发 + 字符串/对象 KV 隔离。
    /// </summary>
    [TestClass]
    public class StateStoreTests
    {
        private static StateStore CreateStore() => new(new StorageCell("StateStoreTest"));

        [TestMethod]
        public void Set_Get_String_ShouldRoundTrip()
        {
            var store = CreateStore();
            store.Set("key1", "value1");
            Assert.AreEqual("value1", store.Get("key1"));
            Assert.IsTrue(store.Has("key1"));
        }

        [TestMethod]
        public void Get_MissingKey_ShouldReturnNull()
        {
            var store = CreateStore();
            Assert.IsNull(store.Get("missing"));
            Assert.IsFalse(store.Has("missing"));
        }

        [TestMethod]
        public void Remove_ShouldDeleteKey()
        {
            var store = CreateStore();
            store.Set("key1", "value1");
            store.Remove("key1");
            Assert.IsNull(store.Get("key1"));
            Assert.IsFalse(store.Has("key1"));
        }

        [TestMethod]
        public void RemoveByPrefix_ShouldDeleteMatchingKeysOnly()
        {
            var store = CreateStore();
            store.Set("app.a", "1");
            store.Set("app.b", "2");
            store.Set("other.c", "3");

            store.RemoveByPrefix("app.");

            Assert.IsNull(store.Get("app.a"));
            Assert.IsNull(store.Get("app.b"));
            Assert.AreEqual("3", store.Get("other.c"));
        }

        [TestMethod]
        public void Set_ShouldRaiseStateChanged()
        {
            var store = CreateStore();
            string? changedKey = null;
            object? changedValue = null;
            store.StateChanged += (k, v) => { changedKey = k; changedValue = v; };

            store.Set("key1", "value1");

            Assert.AreEqual("key1", changedKey);
            Assert.AreEqual("value1", changedValue);
        }

        [TestMethod]
        public void GenericObject_ShouldNotOverwriteStringKey()
        {
            var store = CreateStore();
            store.Set("k", "str");
            store.Set<int>("k", 42);

            Assert.AreEqual("str", store.Get("k"));
            Assert.AreEqual(42, store.Get<int>("k"));
            Assert.AreEqual(1, store.ExtendedStates.Count);
            Assert.IsTrue(store.ExtendedStates.ContainsKey("k"));
        }

        [TestMethod]
        public void Count_And_GetAllKeys_ShouldCountStringKeysOnly()
        {
            var store = CreateStore();
            store.Set("a", "1");
            store.Set("b", "2");
            store.Set<int>("c", 3);

            Assert.AreEqual(2, store.Count);
            Assert.AreEqual(2, store.GetAllKeys().Count);
        }

        [TestMethod]
        public void GetAll_ShouldReturnStringEntriesOnly()
        {
            var store = CreateStore();
            store.Set("a", "1");
            store.Set<int>("c", 3);

            var all = store.GetAll();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("1", all["a"]);
        }

        [TestMethod]
        public void GetOrAdd_ShouldReturnExisting_OrAddDefault()
        {
            var store = CreateStore();
            Assert.AreEqual("default", store.GetOrAdd("k", "default"));
            Assert.AreEqual("default", store.GetOrAdd("k", "other"));
            Assert.AreEqual("default", store.Get("k"));
        }

        [TestMethod]
        public void GetOrAdd_ExistingNullValue_ShouldNotOverwrite()
        {
            var store = CreateStore();
            store.Set("k", (string?)null);

            Assert.IsNull(store.GetOrAdd("k", "default"));
            Assert.IsNull(store.Get("k"));
        }

        [TestMethod]
        public void Clear_ShouldRemoveAllKv()
        {
            var store = CreateStore();
            store.Set("a", "1");
            store.Set<int>("b", 2);

            store.Clear();

            Assert.IsTrue(store.IsEmpty);
            Assert.AreEqual(0, store.Count);
            Assert.AreEqual(0, store.ExtendedStates.Count);
        }

        [TestMethod]
        public void Reset_ShouldClearObjectKv_ButKeepStringKv()
        {
            var store = CreateStore();
            store.Set("a", "1");
            store.Set<int>("b", 2);

            store.Reset();

            Assert.AreEqual("1", store.Get("a"));
            Assert.AreEqual(0, store.ExtendedStates.Count);
            Assert.AreEqual(1, store.Count);
        }
    }
}
