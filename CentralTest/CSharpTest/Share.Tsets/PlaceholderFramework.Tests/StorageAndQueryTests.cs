using CentralConsole.Testing;
using CentralConsole.Testing.Helpers;
using Common.PlaceholderFramework.Core;
using Common.PlaceholderFramework.Integrated.Query;
using Common.PlaceholderFramework.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PlaceholderFramework.Tests
{
    [TestClass]
    public class StorageAndQueryTests : TestBase
    {
        private static PlaceholderMetadata CreateMeta(string id, string batchId, string key,
            string kind = "Input", string module = "TestModule", string handler = "TestHandler",
            string value = "test-value", string nodeId = null, int? nodeIndex = null)
        {
            return new PlaceholderMetadata
            {
                MetadataId = id,
                BatchId = batchId,
                Key = key,
                Kind = kind,
                SourceModule = module,
                HandlerName = handler,
                ResolvedValue = value,
                NodeId = nodeId,
                NodeIndex = nodeIndex,
                Timestamp = DateTime.UtcNow
            };
        }

        [TestMethod]
        public void RunAll_MemoryMetadataStore()
        {
            var result = RunTestGroup("MemoryMetadataStore", builder =>
            {
                builder.Add("Save_ShouldStoreMetadata", () =>
                {
                    var store = new MemoryMetadataStore();
                    var meta = CreateMeta("id1", "batch1", "key1");

                    store.Save(meta);

                    var all = store.FindByBatch("batch1").ToList();
                    return all.Count == 1 && all[0].MetadataId == "id1";
                });

                builder.Add("Save_Multiple_ShouldStoreAll", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1"));
                    store.Save(CreateMeta("id2", "batch1", "key2"));

                    var all = store.FindByBatch("batch1").ToList();
                    return all.Count == 2;
                });

                builder.Add("Save_DifferentBatches_ShouldSeparate", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1"));
                    store.Save(CreateMeta("id2", "batch2", "key2"));

                    return store.FindByBatch("batch1").Count() == 1
                        && store.FindByBatch("batch2").Count() == 1;
                });

                builder.Add("FindByModule_ShouldFilter", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1", module: "ModuleA"));
                    store.Save(CreateMeta("id2", "batch1", "key2", module: "ModuleB"));
                    store.Save(CreateMeta("id3", "batch1", "key3", module: "ModuleA"));

                    var result = store.FindByModule("batch1", "ModuleA").ToList();
                    return result.Count == 2
                        && result.All(m => m.SourceModule == "ModuleA");
                });

                builder.Add("FindByKind_ShouldFilter", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1", kind: "Input"));
                    store.Save(CreateMeta("id2", "batch1", "key2", kind: "Prev"));
                    store.Save(CreateMeta("id3", "batch1", "key3", kind: "Input"));

                    var result = store.FindByKind("batch1", "Input").ToList();
                    return result.Count == 2
                        && result.All(m => m.Kind == "Input");
                });

                builder.Add("FindByKey_ShouldFilter", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "targetKey"));
                    store.Save(CreateMeta("id2", "batch1", "otherKey"));

                    var result = store.FindByKey("batch1", "targetKey").ToList();
                    return result.Count == 1 && result[0].Key == "targetKey";
                });

                builder.Add("FindByNodeId_ShouldFilter", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1", nodeId: "node1"));
                    store.Save(CreateMeta("id2", "batch1", "key2", nodeId: "node2"));

                    var result = store.FindByNodeId("batch1", "node1").ToList();
                    return result.Count == 1 && result[0].NodeId == "node1";
                });

                builder.Add("FindByTimeRange_ShouldFilter", () =>
                {
                    var store = new MemoryMetadataStore();
                    var now = DateTime.UtcNow;
                    var meta1 = CreateMeta("id1", "batch1", "key1");
                    meta1.Timestamp = now.AddMinutes(-10);
                    var meta2 = CreateMeta("id2", "batch1", "key2");
                    meta2.Timestamp = now;
                    var meta3 = CreateMeta("id3", "batch1", "key3");
                    meta3.Timestamp = now.AddMinutes(10);

                    store.Save(meta1);
                    store.Save(meta2);
                    store.Save(meta3);

                    var result = store.FindByTimeRange("batch1",
                        now.AddMinutes(-5), now.AddMinutes(5)).ToList();
                    return result.Count == 1 && result[0].MetadataId == "id2";
                });

                builder.Add("GetAllBatchIds_ShouldReturnAll", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1"));
                    store.Save(CreateMeta("id2", "batch2", "key2"));
                    store.Save(CreateMeta("id3", "batch3", "key3"));

                    var ids = store.GetAllBatchIds().ToList();
                    return ids.Count == 3
                        && ids.Contains("batch1")
                        && ids.Contains("batch2")
                        && ids.Contains("batch3");
                });

                builder.Add("SaveBatch_ShouldStoreAllAtOnce", () =>
                {
                    var store = new MemoryMetadataStore();
                    var items = new[]
                    {
                        CreateMeta("id1", "batch1", "key1"),
                        CreateMeta("id2", "batch1", "key2"),
                        CreateMeta("id3", "batch1", "key3")
                    };

                    store.SaveBatch(items);

                    var all = store.FindByBatch("batch1").ToList();
                    return all.Count == 3;
                });

                builder.Add("GetCount_ShouldReturnCorrectCount", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1"));
                    store.Save(CreateMeta("id2", "batch1", "key2"));
                    store.Save(CreateMeta("id3", "batch1", "key3"));

                    return store.GetCount("batch1") == 3;
                });

                builder.Add("GetCount_EmptyBatch_ShouldReturnZero", () =>
                {
                    var store = new MemoryMetadataStore();
                    return store.GetCount("non-existent") == 0;
                });

                builder.Add("ClearBatch_ShouldRemoveBatch", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1"));
                    store.Save(CreateMeta("id2", "batch1", "key2"));

                    store.ClearBatch("batch1");

                    return store.GetCount("batch1") == 0
                        && store.FindByBatch("batch1").Count() == 0;
                });

                builder.Add("ClearBatch_NonExistent_ShouldNotThrow", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.ClearBatch("non-existent");
                    return true;
                });

                builder.Add("ClearAll_ShouldRemoveAllBatches", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1"));
                    store.Save(CreateMeta("id2", "batch2", "key2"));
                    store.Save(CreateMeta("id3", "batch3", "key3"));

                    store.ClearAll();

                    return store.GetAllBatchIds().Count() == 0;
                });

                builder.Add("SaveBatchAsync_ShouldStoreAllAtOnce", () =>
                {
                    var store = new MemoryMetadataStore();
                    var items = new[]
                    {
                        CreateMeta("id1", "batch1", "key1"),
                        CreateMeta("id2", "batch1", "key2")
                    };

                    store.SaveBatchAsync(items).GetAwaiter().GetResult();

                    return store.FindByBatch("batch1").Count() == 2;
                });

                builder.Add("FindByModule_NonExistent_ShouldReturnEmpty", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1", module: "ModuleA"));

                    return store.FindByModule("batch1", "NonExistent").Count() == 0;
                });

                builder.Add("FindByKind_NonExistent_ShouldReturnEmpty", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1", kind: "Input"));

                    return store.FindByKind("batch1", "Output").Count() == 0;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PlaceholderQuery()
        {
            var result = RunTestGroup("PlaceholderQuery", builder =>
            {
                builder.Add("GetBatchStatistics_ShouldCalculateCorrectly", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1", kind: "Input", module: "ModA"));
                    store.Save(CreateMeta("id2", "batch1", "key2", kind: "Input", module: "ModA"));
                    store.Save(CreateMeta("id3", "batch1", "key3", kind: "Prev", module: "ModB"));

                    var query = new PlaceholderQuery(store);
                    var stats = query.GetBatchStatistics("batch1");

                    return stats.TotalCount == 3
                        && stats.KindDistribution["Input"] == 2
                        && stats.KindDistribution["Prev"] == 1
                        && stats.ModuleDistribution["ModA"] == 2
                        && stats.ModuleDistribution["ModB"] == 1;
                });

                builder.Add("GetBatchStatistics_EmptyBatch_ShouldReturnZero", () =>
                {
                    var store = new MemoryMetadataStore();
                    var query = new PlaceholderQuery(store);
                    var stats = query.GetBatchStatistics("empty-batch");

                    return stats.TotalCount == 0;
                });

                builder.Add("FormatResults_ShouldFormatCorrectly", () =>
                {
                    var store = new MemoryMetadataStore();
                    store.Save(CreateMeta("id1", "batch1", "key1", value: "val1"));

                    var query = new PlaceholderQuery(store);
                    var results = store.FindByBatch("batch1");
                    var formatted = query.FormatResults(results);

                    return formatted.Contains("Found 1 result(s)")
                        && formatted.Contains("key1")
                        && formatted.Contains("val1");
                });

                builder.Add("FormatResults_Empty_ShouldReturnNoResults", () =>
                {
                    var store = new MemoryMetadataStore();
                    var query = new PlaceholderQuery(store);
                    var formatted = query.FormatResults(Enumerable.Empty<PlaceholderMetadata>());

                    return formatted.Contains("No results found");
                });

                builder.Add("GetBatchStatistics_ShouldIncludeTimeRange", () =>
                {
                    var store = new MemoryMetadataStore();
                    var now = DateTime.UtcNow;
                    var meta1 = CreateMeta("id1", "batch1", "key1");
                    meta1.Timestamp = now.AddMinutes(-10);
                    var meta2 = CreateMeta("id2", "batch1", "key2");
                    meta2.Timestamp = now.AddMinutes(10);
                    store.Save(meta1);
                    store.Save(meta2);

                    var query = new PlaceholderQuery(store);
                    var stats = query.GetBatchStatistics("batch1");

                    return stats.TimeRange.HasValue
                        && stats.TimeRange.Value.Min.HasValue
                        && stats.TimeRange.Value.Max.HasValue;
                });

                builder.Add("GetBatchStatistics_EmptyBatch_TimeRangeShouldBeNull", () =>
                {
                    var store = new MemoryMetadataStore();
                    var query = new PlaceholderQuery(store);
                    var stats = query.GetBatchStatistics("empty-batch");

                    return stats.TimeRange == null;
                });
            });

            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PlaceholderReverseResolver()
        {
            var result = RunTestGroup("PlaceholderReverseResolver", builder =>
            {
                builder.Add("RebuildCallChain_ShouldOrderByTimestamp", () =>
                {
                    var store = new MemoryMetadataStore();
                    var now = DateTime.UtcNow;
                    var meta1 = CreateMeta("id1", "batch1", "key1");
                    meta1.Timestamp = now.AddMinutes(-5);
                    var meta2 = CreateMeta("id2", "batch1", "key2");
                    meta2.Timestamp = now;
                    var meta3 = CreateMeta("id3", "batch1", "key3");
                    meta3.Timestamp = now.AddMinutes(5);

                    store.Save(meta3);
                    store.Save(meta1);
                    store.Save(meta2);

                    var resolver = new PlaceholderReverseResolver(store);
                    var chain = resolver.RebuildCallChain("batch1");

                    return chain.Steps.Count == 3
                        && chain.Steps[0].MetadataId == "id1"
                        && chain.Steps[1].MetadataId == "id2"
                        && chain.Steps[2].MetadataId == "id3";
                });

                builder.Add("RebuildCallChain_EmptyBatch_ShouldReturnEmpty", () =>
                {
                    var store = new MemoryMetadataStore();
                    var resolver = new PlaceholderReverseResolver(store);
                    var chain = resolver.RebuildCallChain("empty");

                    return chain.Steps.Count == 0;
                });

                builder.Add("RebuildDataFlow_ShouldBuildGraph", () =>
                {
                    var store = new MemoryMetadataStore();
                    var now = DateTime.UtcNow;
                    var input = CreateMeta("id1", "batch1", "inputKey", kind: "Input");
                    input.Timestamp = now;
                    var prev = CreateMeta("id2", "batch1", "prevKey", kind: "Prev");
                    prev.Timestamp = now.AddMinutes(1);

                    store.Save(input);
                    store.Save(prev);

                    var resolver = new PlaceholderReverseResolver(store);
                    var graph = resolver.RebuildDataFlow("batch1");

                    return graph.Nodes.Count == 2
                        && graph.Inputs.Count == 1
                        && graph.Inputs[0] == "id1";
                });

                builder.Add("TraceDataSource_Input_ShouldReturnExternalInput", () =>
                {
                    var store = new MemoryMetadataStore();
                    var meta = CreateMeta("id1", "batch1", "inputKey", kind: "Input");
                    store.Save(meta);

                    var resolver = new PlaceholderReverseResolver(store);
                    var trace = resolver.TraceDataSource("batch1", "id1");

                    return trace != null
                        && trace.SourceType == "ExternalInput"
                        && trace.TargetKey == "inputKey";
                });

                builder.Add("TraceDataSource_NonExistent_ShouldReturnNull", () =>
                {
                    var store = new MemoryMetadataStore();
                    var resolver = new PlaceholderReverseResolver(store);
                    var trace = resolver.TraceDataSource("batch1", "non-existent");

                    return trace == null;
                });

                builder.Add("TraceDataSink_ShouldFindDependents", () =>
                {
                    var store = new MemoryMetadataStore();
                    var now = DateTime.UtcNow;
                    var source = CreateMeta("id1", "batch1", "sourceKey", kind: "Input");
                    source.Timestamp = now;
                    var sink = CreateMeta("id2", "batch1", "sinkKey", kind: "Prev");
                    sink.Timestamp = now.AddMinutes(1);

                    store.Save(source);
                    store.Save(sink);

                    var resolver = new PlaceholderReverseResolver(store);
                    var sinks = resolver.TraceDataSink("batch1", "id1").ToList();

                    return sinks.Count == 1
                        && sinks[0].SinkKey == "sinkKey"
                        && sinks[0].Relation == "prev reference";
                });

                builder.Add("RebuildDataFlow_EmptyBatch_ShouldReturnEmptyGraph", () =>
                {
                    var store = new MemoryMetadataStore();
                    var resolver = new PlaceholderReverseResolver(store);
                    var graph = resolver.RebuildDataFlow("empty");

                    return graph.Nodes.Count == 0
                        && graph.Edges.Count == 0
                        && graph.Inputs.Count == 0;
                });

                builder.Add("TraceDataSink_NonExistent_ShouldReturnEmpty", () =>
                {
                    var store = new MemoryMetadataStore();
                    var resolver = new PlaceholderReverseResolver(store);
                    var sinks = resolver.TraceDataSink("batch1", "non-existent").ToList();

                    return sinks.Count == 0;
                });

                builder.Add("TraceDataSource_Prev_ShouldReturnPreviousStep", () =>
                {
                    var store = new MemoryMetadataStore();
                    var now = DateTime.UtcNow;
                    var input = CreateMeta("id1", "batch1", "inputKey", kind: "Input");
                    input.Timestamp = now;
                    var prev = CreateMeta("id2", "batch1", "prevKey", kind: "Prev");
                    prev.Timestamp = now.AddMinutes(1);

                    store.Save(input);
                    store.Save(prev);

                    var resolver = new PlaceholderReverseResolver(store);
                    var trace = resolver.TraceDataSource("batch1", "id2");

                    return trace != null
                        && trace.SourceType == "PreviousStep"
                        && trace.SourceMetadataId == "id1";
                });
            });

            result.AssertAllPassed();
        }
    }
}
