using MetaCommand;
using MetaCommand.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MetaCommand.Tests.Storage
{
    [TestClass]
    public class MemoryMetadataStoreTests
    {
        private MetadataRecord CreateRecord(string batchId, string kind, string key)
        {
            return new MetadataRecord
            {
                BatchId = batchId,
                Kind = kind,
                Key = key,
                ResolvedValue = $"value_{key}",
                Timestamp = DateTime.UtcNow
            };
        }

        [TestMethod]
        public void Save_And_FindByBatch_ReturnsStoredData()
        {
            var store = new MemoryMetadataStore();
            var record = CreateRecord("batch1", "Input", "key1");
            store.Save(record);

            var results = store.FindByBatch("batch1").ToList();
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("key1", results[0].Key);
        }

        [TestMethod]
        public void SaveBatch_MultipleRecords_StoresAll()
        {
            var store = new MemoryMetadataStore();
            var records = new[]
            {
                CreateRecord("batch1", "Input", "key1"),
                CreateRecord("batch1", "Input", "key2"),
                CreateRecord("batch1", "Input", "key3")
            };
            store.SaveBatch(records);

            var results = store.FindByBatch("batch1").ToList();
            Assert.AreEqual(3, results.Count);
        }

        [TestMethod]
        public void SaveBatchAsync_StoresRecords()
        {
            var store = new MemoryMetadataStore();
            var records = new[]
            {
                CreateRecord("batch1", "Input", "key1")
            };
            store.SaveBatchAsync(records).GetAwaiter().GetResult();

            var results = store.FindByBatch("batch1").ToList();
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void FindByBatch_NonExistentBatch_ReturnsEmpty()
        {
            var store = new MemoryMetadataStore();
            var results = store.FindByBatch("nonExistent");
            Assert.AreEqual(0, results.Count());
        }

        [TestMethod]
        public void FindByModule_FiltersCorrectly()
        {
            var store = new MemoryMetadataStore();
            store.Save(new MetadataRecord { BatchId = "batch1", SourceModule = "ModuleA", Key = "key1" });
            store.Save(new MetadataRecord { BatchId = "batch1", SourceModule = "ModuleB", Key = "key2" });

            var results = store.FindByModule("batch1", "ModuleA").ToList();
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("key1", results[0].Key);
        }

        [TestMethod]
        public void FindByKind_FiltersCorrectly()
        {
            var store = new MemoryMetadataStore();
            store.Save(new MetadataRecord { BatchId = "batch1", Kind = "Input", Key = "key1" });
            store.Save(new MetadataRecord { BatchId = "batch1", Kind = "Output", Key = "key2" });

            var results = store.FindByKind("batch1", "Input").ToList();
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("key1", results[0].Key);
        }

        [TestMethod]
        public void FindByTimeRange_FiltersCorrectly()
        {
            var store = new MemoryMetadataStore();
            var now = DateTime.UtcNow;
            store.Save(new MetadataRecord { BatchId = "batch1", Key = "key1", Timestamp = now.AddHours(-2) });
            store.Save(new MetadataRecord { BatchId = "batch1", Key = "key2", Timestamp = now });

            var results = store.FindByTimeRange("batch1", now.AddHours(-1), now.AddHours(1)).ToList();
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("key2", results[0].Key);
        }

        [TestMethod]
        public void FindByKey_FiltersCorrectly()
        {
            var store = new MemoryMetadataStore();
            store.Save(new MetadataRecord { BatchId = "batch1", Key = "targetKey" });
            store.Save(new MetadataRecord { BatchId = "batch1", Key = "otherKey" });

            var results = store.FindByKey("batch1", "targetKey").ToList();
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void FindByNodeId_FiltersCorrectly()
        {
            var store = new MemoryMetadataStore();
            store.Save(new MetadataRecord { BatchId = "batch1", NodeId = "node1", Key = "key1" });
            store.Save(new MetadataRecord { BatchId = "batch1", NodeId = "node2", Key = "key2" });

            var results = store.FindByNodeId("batch1", "node1").ToList();
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void GetAllBatchIds_ReturnsAllBatches()
        {
            var store = new MemoryMetadataStore();
            store.Save(CreateRecord("batch1", "Input", "key1"));
            store.Save(CreateRecord("batch2", "Input", "key2"));

            var batchIds = store.GetAllBatchIds().ToList();
            Assert.AreEqual(2, batchIds.Count);
            Assert.IsTrue(batchIds.Contains("batch1"));
            Assert.IsTrue(batchIds.Contains("batch2"));
        }

        [TestMethod]
        public void GetCount_ReturnsCorrectCount()
        {
            var store = new MemoryMetadataStore();
            store.Save(CreateRecord("batch1", "Input", "key1"));
            store.Save(CreateRecord("batch1", "Input", "key2"));
            Assert.AreEqual(2, store.GetCount("batch1"));
        }

        [TestMethod]
        public void GetCount_NonExistentBatch_ReturnsZero()
        {
            var store = new MemoryMetadataStore();
            Assert.AreEqual(0, store.GetCount("nonExistent"));
        }

        [TestMethod]
        public void ClearBatch_RemovesBatchData()
        {
            var store = new MemoryMetadataStore();
            store.Save(CreateRecord("batch1", "Input", "key1"));
            store.ClearBatch("batch1");
            Assert.AreEqual(0, store.GetCount("batch1"));
        }

        [TestMethod]
        public void ClearAll_RemovesAllData()
        {
            var store = new MemoryMetadataStore();
            store.Save(CreateRecord("batch1", "Input", "key1"));
            store.Save(CreateRecord("batch2", "Input", "key2"));
            store.ClearAll();
            Assert.AreEqual(0, store.GetAllBatchIds().Count());
        }
    }
}
