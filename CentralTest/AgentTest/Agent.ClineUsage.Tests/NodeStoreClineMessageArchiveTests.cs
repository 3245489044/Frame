using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Modules;
using Operation.DataStoreDriven.Abstractions;

namespace Agent.Modules.Tests
{
    /// <summary>
    /// NodeStoreClineMessageArchive 单元测试：验证 ur_nodes 归档的「归档后可取回原文 / 幂等 upsert / 轮次升序 / 最近一轮」。
    /// 不依赖真实 SQLite / node，离线运行（用内存版 INodeStore 假实现）。
    /// </summary>
    [TestClass]
    [TestCategory("Modules_Unit")]
    public class NodeStoreClineMessageArchiveTests
    {
        [TestMethod]
        public async Task ArchiveAsync_归档后_LoadAsync可取回原文()
        {
            var archive = new NodeStoreClineMessageArchive(new FakeNodeStore());
            const string json = """[{"role":"user","content":"你好"},{"role":"assistant","content":"你好！"}]""";

            await archive.ArchiveAsync("sess-1", 1, json, "completed", CancellationToken.None);

            var loaded = await archive.LoadAsync("sess-1", 1, CancellationToken.None);
            Assert.AreEqual(json, loaded, "LoadAsync 应取回原文");
        }

        [TestMethod]
        public async Task ArchiveAsync_同轮重复归档_幂等upsert不重复()
        {
            var store = new FakeNodeStore();
            var archive = new NodeStoreClineMessageArchive(store);

            await archive.ArchiveAsync("sess-1", 1, """[{"role":"user","content":"a"}]""", "completed", CancellationToken.None);
            await archive.ArchiveAsync("sess-1", 1, """[{"role":"user","content":"b"}]""", "completed", CancellationToken.None);

            var all = await store.ListAsync(nodeType: NodeStoreClineMessageArchive.NodeType, threadId: "sess-1");
            Assert.AreEqual(1, all.Count, "同轮重复归档应仅 1 条节点");
            Assert.AreEqual("""[{"role":"user","content":"b"}]""", all[0].PayloadJson, "幂等 upsert 应覆盖为新载荷");
        }

        [TestMethod]
        public async Task ArchiveAsync_多轮归档_ListRounds升序_LoadLatest取最大轮()
        {
            var archive = new NodeStoreClineMessageArchive(new FakeNodeStore());

            await archive.ArchiveAsync("sess-1", 1, "r1", "completed", CancellationToken.None);
            await archive.ArchiveAsync("sess-1", 3, "r3", "completed", CancellationToken.None);
            await archive.ArchiveAsync("sess-1", 2, "r2", "completed", CancellationToken.None);

            var rounds = await archive.ListRoundsAsync("sess-1", CancellationToken.None);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, rounds.ToList(), "轮次应按升序排列");

            var latest = await archive.LoadLatestAsync("sess-1", CancellationToken.None);
            Assert.AreEqual("r3", latest, "最近一轮应为最大 round");
        }

        [TestMethod]
        public async Task LoadLatestAsync_无归档_返回null()
        {
            var archive = new NodeStoreClineMessageArchive(new FakeNodeStore());

            var latest = await archive.LoadLatestAsync("sess-empty", CancellationToken.None);
            Assert.IsNull(latest, "无归档时应返回 null");
        }

        /// <summary>内存版 INodeStore（NodeKey 幂等 upsert；ListAsync 按 nodeType/threadId 过滤）。</summary>
        private sealed class FakeNodeStore : INodeStore
        {
            private readonly Dictionary<string, NodeRecord> _nodes = new();
            private long _id;

            public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

            public Task<long> PutAsync(NodeRecord node, CancellationToken ct = default)
            {
                if (_nodes.TryGetValue(node.NodeKey, out var existing))
                {
                    existing.PayloadJson = node.PayloadJson;
                    existing.Status = node.Status;
                    existing.UpdatedAt = node.UpdatedAt ?? existing.UpdatedAt;
                    return Task.FromResult(existing.Id);
                }
                var clone = new NodeRecord
                {
                    Id = ++_id,
                    NodeKey = node.NodeKey,
                    NodeType = node.NodeType,
                    ThreadId = node.ThreadId,
                    Key = node.Key,
                    Status = node.Status,
                    PayloadJson = node.PayloadJson,
                    CreatedAt = node.CreatedAt,
                    UpdatedAt = node.UpdatedAt,
                };
                _nodes[clone.NodeKey] = clone;
                return Task.FromResult(clone.Id);
            }

            public Task<NodeRecord?> GetAsync(string nodeKey, CancellationToken ct = default)
                => Task.FromResult(_nodes.TryGetValue(nodeKey, out var n) ? n : null);

            public Task<IReadOnlyList<NodeRecord>> ListAsync(string? nodeType = null, string? threadId = null,
                long? afterId = null, int limit = 100, CancellationToken ct = default)
            {
                IEnumerable<NodeRecord> query = _nodes.Values;
                if (!string.IsNullOrWhiteSpace(nodeType)) query = query.Where(n => n.NodeType == nodeType);
                if (!string.IsNullOrWhiteSpace(threadId)) query = query.Where(n => n.ThreadId == threadId);
                if (afterId.HasValue) query = query.Where(n => n.Id > afterId.Value);
                var list = query.OrderBy(n => n.Id).Take(limit).ToList();
                return Task.FromResult<IReadOnlyList<NodeRecord>>(list);
            }

            public Task<bool> DeleteAsync(string nodeKey, CancellationToken ct = default)
                => Task.FromResult(_nodes.Remove(nodeKey));

            public Task<IReadOnlyList<string>> DeleteManyAsync(IEnumerable<string> nodeKeys, CancellationToken ct = default)
            {
                var removed = nodeKeys.Where(_nodes.Remove).ToList();
                return Task.FromResult<IReadOnlyList<string>>(removed);
            }
        }
    }
}
